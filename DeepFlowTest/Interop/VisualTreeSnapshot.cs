namespace DeepFlowTest.Interop;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

public sealed record class VisualTreeSnapshot
{
	public long SequenceNumber { get; set; }

	public int ProcessId { get; set; } = Process.GetCurrentProcess().Id;

	public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;

	public List<string> RootIds { get; set; } = [];

	public List<VisualTreeNodeDto> Nodes { get; set; } = [];

	public int NodeCount { get; set; }

	public List<string> RequestedPropertyNames { get; set; } = [];

	public List<string> RequestedProperties
	{
		get => RequestedPropertyNames;
		set => RequestedPropertyNames = value ?? [];
	}

	public bool ShouldSerializeRequestedProperties() => false;

	public Dictionary<string, object?> Metadata { get; set; } = [];

	public string TargetFrameworkFamily { get; set; } = string.Empty;

	public bool IsTruncated { get; set; }

	public string? TruncationReason { get; set; }

	public static VisualTreeSnapshot Create(
		long sequenceNumber,
		IEnumerable<VisualTreeNodeDto> nodes,
		IEnumerable<string>? requestedPropertyNames = null,
		string targetFrameworkFamily = "",
		bool isTruncated = false,
		string? truncationReason = null)
	{
		var nodeList = nodes.ToList();
		var generatedUtc = DateTimeOffset.UtcNow;
		return new VisualTreeSnapshot
		{
			SequenceNumber = sequenceNumber,
			GeneratedUtc = generatedUtc,
			RootIds = nodeList.Where(static node => node.IsRoot || node.ParentId is null).Select(static node => node.TargetId).ToList(),
			Nodes = nodeList,
			NodeCount = nodeList.Count,
			RequestedPropertyNames = requestedPropertyNames?.ToList() ?? [],
			Metadata = new Dictionary<string, object?>
			{
				["nodeCount"] = nodeList.Count,
				["generatedUtc"] = generatedUtc,
				["targetFrameworkFamily"] = targetFrameworkFamily,
				["isTruncated"] = isTruncated,
				["truncationReason"] = truncationReason,
			},
			TargetFrameworkFamily = targetFrameworkFamily,
			IsTruncated = isTruncated,
			TruncationReason = truncationReason,
		};
	}

	public static VisualTreeSnapshot FromNodes(
		IReadOnlyList<Dictionary<string, object?>> nodes,
		IEnumerable<string> requestedProperties)
	{
		_ = nodes ?? throw new ArgumentNullException(nameof(nodes));
		_ = requestedProperties ?? throw new ArgumentNullException(nameof(requestedProperties));

		var nodeDtos = nodes.Select(ToNodeDto).ToArray();
		return Create(
			0,
			nodeDtos,
			requestedProperties.OrderBy(static property => property, StringComparer.Ordinal));
	}

	private static VisualTreeNodeDto ToNodeDto(Dictionary<string, object?> node)
	{
		var framework = node.TryGetValue("FrameworkTypeName", out var frameworkTypeName)
			? frameworkTypeName
			: node.TryGetValue("Framework", out var frameworkValue)
				? frameworkValue
				: null;
		return new VisualTreeNodeDto
		{
			TargetId = ToRequiredString(node, "TargetId"),
			TypeName = ToRequiredString(node, "TypeName"),
			ParentId = ToOptionalString(node.TryGetValue("ParentId", out var parentId) ? parentId : null),
			ChildIds = ToStringList(node.TryGetValue("ChildIds", out var childIds) ? childIds : null),
			Properties = ToPropertyDictionary(node.TryGetValue("Properties", out var properties) ? properties : null),
			FrameworkTypeName = ToOptionalString(framework),
		};
	}

	private static string ToRequiredString(Dictionary<string, object?> node, string key)
	{
		if (!node.TryGetValue(key, out var value) || value is null)
			throw new InvalidOperationException($"Visual tree node is missing `{key}`.");

		return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
	}

	private static string? ToOptionalString(object? value) =>
		value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

	private static List<string> ToStringList(object? value)
	{
		if (value is null)
			return [];
		if (value is string stringValue)
			return [stringValue];
		if (value is IEnumerable enumerable)
		{
			return enumerable
				.Cast<object?>()
				.Where(static item => item is not null)
				.Select(static item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty)
				.ToList();
		}

		return [Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty];
	}

	private static Dictionary<string, object?> ToPropertyDictionary(object? value)
	{
		var result = new Dictionary<string, object?>(StringComparer.Ordinal);
		if (value is null)
			return result;

		if (value is IDictionary genericDictionary)
		{
			foreach (var entryObject in genericDictionary)
			{
				if (entryObject is not DictionaryEntry entry)
					continue;

				if (entry.Key is not null)
					result[entry.Key.ToString()!] = UnwrapPropertyValue(entry.Value);
			}
		}

		return result;
	}

	private static object? UnwrapPropertyValue(object? value)
	{
		if (value is null)
			return null;

		var property = value.GetType().GetProperty("Value");
		return property is null ? value : property.GetValue(value);
	}
}
