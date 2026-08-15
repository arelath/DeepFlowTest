namespace DeepFlowTest.Automation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class FindSnapshotOptions
{
	public string? TypeName { get; set; }

	public string? TypeContains { get; set; }

	public string? Name { get; set; }

	public string? AutomationId { get; set; }

	public string? Text { get; set; }

	public KeyValuePair<string, string>? PropertyEquals { get; set; }

	public KeyValuePair<string, string>? PropertyContains { get; set; }

	public KeyValuePair<string, string>? PropertyRegex { get; set; }

	public bool? Visible { get; set; }

	public bool? Enabled { get; set; }

	public bool CaseSensitive { get; set; }

	public int Limit { get; set; } = 50;

	public bool IncludePath { get; set; }

	public bool IncludeProperties { get; set; }

	public bool IncludeChildren { get; set; }

	public bool IncludeAncestors { get; set; }

	public bool UseShortIds { get; set; } = true;

	public IReadOnlyList<string> Properties { get; set; } = [];
}

public sealed class FindSnapshotService(TreeSnapshotService? treeService = null)
{
	private readonly TreeSnapshotService treeService = treeService ?? new TreeSnapshotService();

	public FindResultData Find(VisualTreeSnapshot snapshot, FindSnapshotOptions options)
	{
		_ = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
		_ = options ?? throw new ArgumentNullException(nameof(options));

		var regex = CompileRegex(options.PropertyRegex);
		var relationships = SnapshotRelationships.Create(snapshot);
		var matches = snapshot.Nodes
			.Where(node => Matches(node, options, regex))
			.Take(Math.Max(0, options.Limit))
			.Select(node => ToMatch(node, snapshot, relationships, options))
			.ToList();

		return new FindResultData
		{
			MatchCount = matches.Count,
			MaxMatches = Math.Max(0, options.Limit),
			Matches = matches,
		};
	}

	private FindMatchData ToMatch(VisualTreeNodeDto node, VisualTreeSnapshot snapshot, SnapshotRelationships relationships, FindSnapshotOptions options)
	{
		var nodeOptions = new TreeSnapshotOptions
		{
			IncludePath = options.IncludePath,
			IncludeTypeNames = true,
			UseShortIds = options.UseShortIds,
			Properties = options.IncludeProperties ? options.Properties : [],
		};
		return new FindMatchData
		{
			Node = treeService.ShapeOne(node, snapshot, nodeOptions),
			Ancestors = options.IncludeAncestors
				? relationships.AncestorsOf(node.TargetId).Select(ancestor => treeService.ShapeOne(ancestor, snapshot, nodeOptions)).ToList()
				: [],
			Children = options.IncludeChildren
				? relationships.ChildrenOf(node.TargetId).Select(child => treeService.ShapeOne(child, snapshot, nodeOptions)).ToList()
				: [],
		};
	}

	private static bool Matches(VisualTreeNodeDto node, FindSnapshotOptions options, Regex? propertyRegex)
	{
		var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
		if (!string.IsNullOrWhiteSpace(options.TypeName)
			&& !EqualsAny(options.TypeName!, comparison, node.TypeName, node.FrameworkTypeName, ShortTypeName(node.FrameworkTypeName)))
			return false;

		if (!string.IsNullOrWhiteSpace(options.TypeContains)
			&& !ContainsAny(options.TypeContains!, comparison, node.TypeName, node.FrameworkTypeName))
			return false;

		if (!string.IsNullOrWhiteSpace(options.Name)
			&& !PropertyEqualsAny(node, options.Name!, comparison, KnownProperties.Name, KnownProperties.AutomationName))
			return false;

		if (!string.IsNullOrWhiteSpace(options.AutomationId)
			&& !PropertyEqualsAny(node, options.AutomationId!, comparison, KnownProperties.AutomationId, KnownProperties.AutomationIdAlias, KnownProperties.Id))
			return false;

		if (!string.IsNullOrWhiteSpace(options.Text)
			&& !PropertyEqualsAny(node, options.Text!, comparison, KnownProperties.Text, KnownProperties.Content, KnownProperties.Header, KnownProperties.Title))
			return false;

		if (options.PropertyEquals is { } equals
			&& !PropertyEqualsAny(node, equals.Value, comparison, equals.Key))
			return false;

		if (options.PropertyContains is { } contains
			&& !PropertyContainsAny(node, contains.Value, comparison, contains.Key))
			return false;

		if (options.PropertyRegex is { } regexPair
			&& !PropertyRegexAny(node, regexPair.Key, propertyRegex!))
			return false;

		if (options.Visible.HasValue && !PropertyBoolEquals(node, KnownProperties.IsVisible, options.Visible.Value))
			return false;

		if (options.Enabled.HasValue && !PropertyBoolEquals(node, KnownProperties.IsEnabled, options.Enabled.Value))
			return false;

		return true;
	}

	private static Regex? CompileRegex(KeyValuePair<string, string>? regexPair)
	{
		if (regexPair is null)
			return null;

		try
		{
			return new Regex(regexPair.Value.Value, RegexOptions.CultureInvariant);
		}
		catch (ArgumentException ex)
		{
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Invalid regex: {ex.Message}");
		}
	}

	private static bool EqualsAny(string expected, StringComparison comparison, params string?[] values) =>
		values.Any(value => string.Equals(value, expected, comparison));

	private static bool ContainsAny(string expected, StringComparison comparison, params string?[] values) =>
		values.Any(value => value?.Contains(expected, comparison) == true);

	private static bool PropertyEqualsAny(VisualTreeNodeDto node, string expected, StringComparison comparison, params string[] names) =>
		names.Any(name => node.Properties.TryGetValue(name, out var actual)
			&& string.Equals(Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture), expected, comparison));

	private static bool PropertyContainsAny(VisualTreeNodeDto node, string expected, StringComparison comparison, params string[] names) =>
		names.Any(name => node.Properties.TryGetValue(name, out var actual)
			&& Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture)?.Contains(expected, comparison) == true);

	private static bool PropertyRegexAny(VisualTreeNodeDto node, string name, Regex regex) =>
		node.Properties.TryGetValue(name, out var actual)
		&& actual is not null
		&& regex.IsMatch(Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);

	private static bool PropertyBoolEquals(VisualTreeNodeDto node, string name, bool expected)
	{
		if (!node.Properties.TryGetValue(name, out var actual) || actual is null)
			return false;

		if (actual is bool boolValue)
			return boolValue == expected;

		return bool.TryParse(Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture), out var parsed) && parsed == expected;
	}

	private static string? ShortTypeName(string? typeName)
	{
		if (string.IsNullOrWhiteSpace(typeName))
			return null;

		var index = typeName.LastIndexOf('.');
		return index >= 0 ? typeName[(index + 1)..] : typeName;
	}
}

public sealed class FindResultData
{
	public int MatchCount { get; set; }

	public int MaxMatches { get; set; }

	public IReadOnlyList<FindMatchData> Matches { get; set; } = [];
}

public sealed class FindMatchData
{
	public TreeNodeData Node { get; set; } = new();

	public IReadOnlyList<TreeNodeData> Ancestors { get; set; } = [];

	public IReadOnlyList<TreeNodeData> Children { get; set; } = [];
}
