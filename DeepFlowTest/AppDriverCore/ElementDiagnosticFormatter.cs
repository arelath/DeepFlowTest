namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class ElementDiagnosticFormatter
{
	private const int SelectorDescriptionMaxLength = 700;
	private const int ElementSummaryValueMaxLength = 120;
	private const int ElementPathMaxLength = 1_200;
	private static readonly IReadOnlyList<string> SummaryPropertyNames =
	[
		"AutomationProperties.AutomationId",
		"AutomationId",
		"Name",
		"AutomationProperties.Name",
		"Header",
		"Text",
		"Content",
		"IsVisible",
		"IsEnabled",
	];

	private static readonly IReadOnlyList<string> PathIdentityPropertyNames =
	[
		"AutomationProperties.AutomationId",
		"AutomationId",
		"Name",
		"AutomationProperties.Name",
		"Header",
		"Text",
		"Content",
	];

	public static string BuildNoMatchElementMessage(string selectorDescription, Func<string?>? diagnosticProvider)
	{
		var builder = new StringBuilder();
		builder.Append("No element matched selector.");
		builder.AppendLine();
		builder.Append("Selector: ");
		builder.AppendLine(Truncate(NormalizeWhitespace(selectorDescription), SelectorDescriptionMaxLength));
		var diagnostic = TryGetNoMatchDiagnostic(diagnosticProvider);
		if (!string.IsNullOrWhiteSpace(diagnostic))
			builder.AppendLine(diagnostic);

		return builder.ToString().TrimEnd();
	}

	public static string BuildRootNoMatchDiagnostic(
		string rootTargetId,
		VisualTreeSnapshot snapshot,
		IReadOnlyList<Element> elements,
		int maxElementCount)
	{
		var builder = new StringBuilder();
		builder.AppendLine($"Elements currently under '{rootTargetId}' ({snapshot.NodeCount} captured):");
		if (elements.Count == 0)
		{
			builder.Append("  <none>");
			return builder.ToString();
		}

		for (var i = 0; i < elements.Count; i++)
		{
			builder.Append("  ");
			builder.Append(i + 1);
			builder.Append(". ");
			builder.AppendLine(FormatElementSummary(elements[i]));
		}

		if (snapshot.NodeCount > elements.Count)
		{
			builder.Append("  ... ");
			builder.Append(snapshot.NodeCount - elements.Count);
			builder.AppendLine(" more element(s) omitted.");
		}

		if (snapshot.IsTruncated && !string.IsNullOrWhiteSpace(snapshot.TruncationReason))
		{
			builder.Append("  Snapshot truncated: ");
			builder.AppendLine(snapshot.TruncationReason);
		}

		return builder.ToString().TrimEnd();
	}

	public static string BuildAmbiguousElementMessage(string selectorDescription, IReadOnlyList<Element> matches)
	{
		var builder = new StringBuilder();
		builder.Append("More than one element matched selector.");
		builder.AppendLine();
		builder.Append("Selector: ");
		builder.AppendLine(Truncate(NormalizeWhitespace(selectorDescription), SelectorDescriptionMaxLength));
		builder.AppendLine("Matched elements:");
		for (var i = 0; i < matches.Count; i++)
		{
			builder.Append("  ");
			builder.Append(i + 1);
			builder.Append(". ");
			builder.AppendLine(FormatElementSummary(matches[i]));
		}

		builder.Append("Make the selector more specific, or call GetElements when multiple matches are expected.");
		return builder.ToString();
	}

	private static string? TryGetNoMatchDiagnostic(Func<string?>? diagnosticProvider)
	{
		if (diagnosticProvider is null)
			return null;

		try
		{
			return diagnosticProvider();
		}
		catch (Exception ex)
		{
			return $"Diagnostic lookup failed: {ex.GetType().Name}: {ex.Message}";
		}
	}

	private static string FormatElementSummary(Element element)
	{
		var parts = new List<string>
		{
			$"TargetId={FormatDiagnosticValue(element.TargetId)}",
			$"TypeName={FormatDiagnosticValue(element.TypeName)}",
		};

		if (!string.IsNullOrWhiteSpace(element.FrameworkTypeName)
			&& !string.Equals(element.FrameworkTypeName, element.TypeName, StringComparison.Ordinal))
		{
			parts.Add($"FrameworkTypeName={FormatDiagnosticValue(element.FrameworkTypeName)}");
		}

		foreach (var propertyName in SummaryPropertyNames)
		{
			if (!element.Properties.TryGetValue(propertyName, out var value) || value is null or PropertyExtractionError)
				continue;

			var valueText = NormalizeWhitespace(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
			if (valueText.Length == 0)
				continue;

			parts.Add($"{propertyName}={FormatDiagnosticValue(value)}");
		}

		var path = FormatElementPath(element);
		if (path.Length != 0)
			parts.Add($"Path={path}");

		return string.Join(", ", parts);
	}

	private static string FormatElementPath(Element element)
	{
		var path = GetElementPath(element);
		if (path.Count <= 1)
			return string.Empty;

		return Truncate(string.Join(" > ", path.Select(FormatPathSegment)), ElementPathMaxLength);
	}

	private static IReadOnlyList<ElementPathSegmentResponse> GetElementPath(Element element)
	{
		if (element.DiagnosticPath.Count != 0)
			return element.DiagnosticPath;

		var snapshot = element.CurrentSnapshot;
		if (snapshot is null)
			return [];

		var byId = new Dictionary<string, VisualTreeNodeDto>(StringComparer.Ordinal);
		foreach (var candidate in snapshot.Nodes)
		{
			if (!byId.TryGetValue(candidate.TargetId, out var existing) || existing.ParentId is null)
				byId[candidate.TargetId] = candidate;
		}

		var path = new List<ElementPathSegmentResponse>();
		var seenTargetIds = new HashSet<string>(StringComparer.Ordinal);
		var current = element.SnapshotNode;
		while (true)
		{
			if (!seenTargetIds.Add(current.TargetId))
				break;

			path.Add(ToPathSegment(current));
			if (string.IsNullOrWhiteSpace(current.ParentId))
				break;
			if (!byId.TryGetValue(current.ParentId!, out current))
				break;
		}

		path.Reverse();
		return path;
	}

	private static ElementPathSegmentResponse ToPathSegment(VisualTreeNodeDto node) =>
		new()
		{
			TargetId = node.TargetId,
			TypeName = node.TypeName,
			FrameworkTypeName = node.FrameworkTypeName,
			Properties = node.Properties,
		};

	private static string FormatPathSegment(ElementPathSegmentResponse segment)
	{
		var identityParts = PathIdentityPropertyNames
			.Select(propertyName => TryFormatPathProperty(segment, propertyName))
			.Where(static value => value is not null)
			.Cast<string>()
			.Take(3)
			.ToArray();
		return identityParts.Length == 0
			? segment.TypeName
			: $"{segment.TypeName}[{string.Join(", ", identityParts)}]";
	}

	private static string? TryFormatPathProperty(ElementPathSegmentResponse segment, string propertyName)
	{
		if (!segment.Properties.TryGetValue(propertyName, out var value) || value is null or PropertyExtractionError)
			return null;

		var valueText = NormalizeWhitespace(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		if (valueText.Length == 0)
			return null;

		return $"{ShortenPropertyName(propertyName)}={FormatDiagnosticValue(value)}";
	}

	private static string ShortenPropertyName(string propertyName) =>
		propertyName.StartsWith("AutomationProperties.", StringComparison.Ordinal)
			? propertyName.Substring("AutomationProperties.".Length)
			: propertyName;

	private static string FormatDiagnosticValue(object? value)
	{
		if (value is null)
			return "<null>";

		var text = Truncate(NormalizeWhitespace(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty), ElementSummaryValueMaxLength);
		return value is string
			? $"\"{text.Replace("\"", "\\\"")}\""
			: text;
	}

	private static string NormalizeWhitespace(string value)
	{
		if (string.IsNullOrEmpty(value))
			return string.Empty;

		var builder = new StringBuilder(value.Length);
		var previousWasWhitespace = false;
		foreach (var character in value)
		{
			if (char.IsWhiteSpace(character))
			{
				if (!previousWasWhitespace)
					builder.Append(' ');
				previousWasWhitespace = true;
				continue;
			}

			builder.Append(character);
			previousWasWhitespace = false;
		}

		return builder.ToString().Trim();
	}

	private static string Truncate(string value, int maxLength)
	{
		if (value.Length <= maxLength)
			return value;

		const string Suffix = "...";
		return maxLength <= Suffix.Length
			? value.Substring(0, maxLength)
			: value.Substring(0, maxLength - Suffix.Length) + Suffix;
	}
}
