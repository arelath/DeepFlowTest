namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility.WpfUtility.Tree;
using Newtonsoft.Json.Linq;

internal static class CompactSemanticRecordingFrame
{
	private const int MaxRemovedIds = 20;

	private static readonly IReadOnlyDictionary<string, string> PropertyAliases =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[KnownProperties.Name] = "name",
			[KnownProperties.AutomationName] = "automationName",
			[KnownProperties.AutomationNameAlias] = "automationName",
			[KnownProperties.AutomationId] = "automationId",
			[KnownProperties.AutomationIdAlias] = "automationId",
			[KnownProperties.Text] = "text",
			[KnownProperties.Content] = "content",
			[KnownProperties.Header] = "header",
			[KnownProperties.Title] = "title",
			[KnownProperties.Uid] = "uid",
			[KnownProperties.IsChecked] = "checked",
			[KnownProperties.Checked] = "checked",
			[KnownProperties.IsEnabled] = "enabled",
			[KnownProperties.IsExpanded] = "expanded",
			[KnownProperties.IsOpen] = "open",
			[KnownProperties.IsSelected] = "selected",
			[KnownProperties.IsSubmenuOpen] = "submenuOpen",
			[KnownProperties.IsVisible] = "visible",
			[KnownProperties.Visibility] = "visibility",
		};

	public static Dictionary<string, object?> Create(SemanticRecordingFrame frame, CompactSemanticRecordingState? state = null)
	{
		_ = frame ?? throw new ArgumentNullException(nameof(frame));
		var output = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["kind"] = frame.FrameKind,
			["seq"] = frame.SequenceNumber,
			["at"] = frame.TimestampUtc,
		};

		if (string.Equals(frame.FrameKind, "recording-started", StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(frame.RecordingId))
		{
			output["recordingId"] = frame.RecordingId;
		}
		if (frame.Action is not null)
			output["action"] = CompactAction(frame.Action);
		if (frame.Snapshot is not null)
			output["snapshot"] = CompactSnapshot(frame.Snapshot);
		if (frame.Delta is not null)
			output["delta"] = CompactDelta(frame.Delta, state);
		if (frame.Metadata.Count != 0)
			output["metadata"] = CompactMetadata(frame.Metadata);

		state?.Update(frame);
		return output;
	}

	private static Dictionary<string, object?> CompactAction(RecordedInputAction action)
	{
		var output = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["kind"] = action.ActionKind,
		};
		AddIfNotEmpty(output, "mouseButton", action.MouseButton);
		AddIfNotNull(output, "clickCount", action.ClickCount);
		AddIfNotEmpty(output, "text", action.Text);
		AddIfNotEmpty(output, "keys", action.Keys);
		if (!string.IsNullOrWhiteSpace(action.Target.TargetId) || !string.IsNullOrWhiteSpace(action.Target.TypeName))
			output["target"] = CompactTarget(action.Target);
		if (action.Metadata.Count != 0)
			output["metadata"] = CompactMetadata(action.Metadata);
		return output;
	}

	private static Dictionary<string, object?> CompactTarget(RecordedTarget target)
	{
		var output = new Dictionary<string, object?>(StringComparer.Ordinal);
		AddIfNotEmpty(output, "id", target.TargetId);
		AddIfNotEmpty(output, "type", target.TypeName);
		AddIfNotEmpty(output, "summary", target.Summary);
		var properties = CompactProperties(target.Properties);
		if (properties.Count != 0)
			output["props"] = properties;
		if (target.SelectorHints.Count != 0)
		{
			output["selectors"] = target.SelectorHints
				.Select(CompactSelectorHint)
				.Where(static hint => hint.Count != 0)
				.ToArray();
		}

		return output;
	}

	private static Dictionary<string, object?> CompactSelectorHint(RecordedSelectorHint hint)
	{
		var output = new Dictionary<string, object?>(StringComparer.Ordinal);
		AddIfNotEmpty(output, "kind", hint.Kind);
		AddIfNotEmpty(output, "property", PropertyAliases.TryGetValue(hint.PropertyName, out var alias) ? alias : hint.PropertyName);
		AddIfNotNull(output, "value", CompactValue(hint.Value));
		AddIfNotEmpty(output, "cli", hint.Cli);
		if (hint.Confidence > 0)
			output["confidence"] = hint.Confidence;
		return output;
	}

	private static Dictionary<string, object?> CompactSnapshot(VisualTreeSnapshot snapshot)
	{
		var nodes = CompactNodeTree(snapshot.Nodes);
		var includedCount = CountNodes(nodes);
		var output = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["seq"] = snapshot.SequenceNumber,
			["nodeCount"] = snapshot.Nodes.Count,
			["includedCount"] = includedCount,
			["omittedCount"] = Math.Max(0, snapshot.Nodes.Count - includedCount),
			["nodes"] = nodes,
		};
		if (snapshot.IsTruncated)
		{
			output["truncated"] = true;
			AddIfNotEmpty(output, "truncationReason", snapshot.TruncationReason);
		}

		return output;
	}

	private static Dictionary<string, object?> CompactDelta(VisualTreeSnapshotDelta delta, CompactSemanticRecordingState? state)
	{
		var added = CompactNodeTree(delta.Added);
		var changed = CompactChangedNodes(delta.Changed, state);
		var output = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["baseSeq"] = delta.BaseSequenceNumber,
			["currentSeq"] = delta.CurrentSequenceNumber,
			["addedCount"] = delta.Added.Count,
			["changedCount"] = delta.Changed.Count,
			["removedCount"] = delta.RemovedTargetIds.Count,
		};
		if (added.Count != 0)
			output["added"] = added;
		if (changed.Count != 0)
			output["changed"] = changed;
		if (delta.RemovedTargetIds.Count != 0)
		{
			output["removed"] = delta.RemovedTargetIds.Take(MaxRemovedIds).ToArray();
			if (delta.RemovedTargetIds.Count > MaxRemovedIds)
				output["removedOmittedCount"] = delta.RemovedTargetIds.Count - MaxRemovedIds;
		}

		return output;
	}

	private static List<Dictionary<string, object?>> CompactNodeTree(IReadOnlyList<VisualTreeNodeDto> nodes)
	{
		var sourceById = new Dictionary<string, VisualTreeNodeDto>(StringComparer.Ordinal);
		foreach (var node in nodes)
		{
			if (!string.IsNullOrWhiteSpace(node.TargetId))
				sourceById[node.TargetId] = node;
		}

		var compacted = new List<CompactedNode>();
		foreach (var node in nodes)
		{
			var output = CompactNode(node);
			if (output.Count != 0)
				compacted.Add(new CompactedNode(node, output));
		}

		var includedById = new Dictionary<string, CompactedNode>(StringComparer.Ordinal);
		foreach (var node in compacted)
		{
			if (!string.IsNullOrWhiteSpace(node.Source.TargetId))
				includedById[node.Source.TargetId] = node;
		}

		var roots = new List<Dictionary<string, object?>>();
		foreach (var node in compacted)
		{
			var parent = FindNearestIncludedParent(node.Source, sourceById, includedById);
			if (parent is null)
				roots.Add(node.Output);
			else
				AddChild(parent.Output, node.Output);
		}

		return roots;
	}

	private static int CountNodes(IEnumerable<Dictionary<string, object?>> nodes)
	{
		var count = 0;
		foreach (var node in nodes)
		{
			count++;
			if (node.TryGetValue("children", out var children)
				&& children is IEnumerable<Dictionary<string, object?>> childNodes)
			{
				count += CountNodes(childNodes);
			}
		}

		return count;
	}

	private static CompactedNode? FindNearestIncludedParent(
		VisualTreeNodeDto node,
		IReadOnlyDictionary<string, VisualTreeNodeDto> sourceById,
		IReadOnlyDictionary<string, CompactedNode> includedById)
	{
		var visited = new HashSet<string>(StringComparer.Ordinal);
		var parentId = node.ParentId;
		while (!string.IsNullOrWhiteSpace(parentId))
		{
			var currentParentId = parentId!;
			if (!visited.Add(currentParentId))
				break;

			if (includedById.TryGetValue(currentParentId, out var includedParent))
				return includedParent;
			if (!sourceById.TryGetValue(currentParentId, out var sourceParent))
				return null;

			parentId = sourceParent.ParentId;
		}

		return null;
	}

	private static void AddChild(Dictionary<string, object?> parent, Dictionary<string, object?> child)
	{
		if (!parent.TryGetValue("children", out var existing)
			|| existing is not List<Dictionary<string, object?>> children)
		{
			children = [];
			parent["children"] = children;
		}

		children.Add(child);
	}

	private static Dictionary<string, object?> CompactNode(VisualTreeNodeDto node)
	{
		var properties = CompactProperties(node.Properties);
		if (!ShouldIncludeNode(node, properties))
			return [];

		var output = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["id"] = node.TargetId,
			["type"] = node.TypeName,
		};
		if (node.IsRoot)
			output["root"] = true;
		foreach (var property in properties)
			output[property.Key] = property.Value;
		return output;
	}

	private static List<Dictionary<string, object?>> CompactChangedNodes(
		IReadOnlyList<VisualTreeNodeDto> nodes,
		CompactSemanticRecordingState? state)
	{
		var output = new List<Dictionary<string, object?>>();
		foreach (var node in nodes)
		{
			VisualTreeNodeDto? previous = null;
			if (state is not null && state.TryGetPreviousNode(node.TargetId, out var previousNode))
				previous = previousNode;

			var compacted = CompactChangedNode(node, previous);
			if (compacted.Count != 0)
				output.Add(compacted);
		}

		return output;
	}

	private static Dictionary<string, object?> CompactChangedNode(VisualTreeNodeDto current, VisualTreeNodeDto? previous)
	{
		var changes = previous is null
			? CompactProperties(current.Properties, includeDefaultStateValues: true)
			: CompactPropertyChanges(previous.Properties, current.Properties);

		if (previous is not null)
		{
			if (!string.Equals(previous.TypeName, current.TypeName, StringComparison.Ordinal))
				changes["type"] = current.TypeName;
			if (previous.IsRoot != current.IsRoot)
				changes["root"] = current.IsRoot;
		}

		if (changes.Count == 0)
			return [];

		var output = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["id"] = current.TargetId,
		};
		AddIfNotEmpty(output, "type", current.TypeName);
		foreach (var property in CompactProperties(current.Properties))
		{
			if (IsIdentityProperty(property.Key) && !changes.ContainsKey(property.Key))
				output[property.Key] = property.Value;
		}

		output["changes"] = changes;
		return output;
	}

	private static bool ShouldIncludeNode(VisualTreeNodeDto node, IReadOnlyDictionary<string, object?> compactProperties)
	{
		if (node.IsRoot)
			return true;

		if (compactProperties.Keys.Any(IsIdentityProperty))
			return true;

		return (compactProperties.TryGetValue("visible", out var visible) && visible is false)
			|| (compactProperties.TryGetValue("enabled", out var enabled) && enabled is false)
			|| compactProperties.ContainsKey("checked")
			|| compactProperties.ContainsKey("expanded")
			|| compactProperties.ContainsKey("open")
			|| compactProperties.ContainsKey("selected")
			|| compactProperties.ContainsKey("submenuOpen")
			|| string.Equals(node.TypeName, "Window", StringComparison.Ordinal)
			|| string.Equals(node.TypeName, "Dialog", StringComparison.Ordinal);
	}

	private static bool IsIdentityProperty(string propertyName) =>
		propertyName is "name" or "automationName" or "automationId" or "text" or "content" or "header" or "title" or "uid";

	private static Dictionary<string, object?> CompactPropertyChanges(
		IReadOnlyDictionary<string, object?> previous,
		IReadOnlyDictionary<string, object?> current)
	{
		var previousProperties = CompactProperties(previous, includeDefaultStateValues: true);
		var currentProperties = CompactProperties(current, includeDefaultStateValues: true);
		var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var key in previousProperties.Keys.Concat(currentProperties.Keys).Distinct(StringComparer.Ordinal).OrderBy(static key => key, StringComparer.Ordinal))
		{
			previousProperties.TryGetValue(key, out var previousValue);
			currentProperties.TryGetValue(key, out var currentValue);
			if (!ValuesEqual(previousValue, currentValue))
				changes[key] = currentProperties.ContainsKey(key) ? currentValue : null;
		}

		return changes;
	}

	private static Dictionary<string, object?> CompactProperties(IReadOnlyDictionary<string, object?> properties) =>
		CompactProperties(properties, includeDefaultStateValues: false);

	private static Dictionary<string, object?> CompactProperties(IReadOnlyDictionary<string, object?> properties, bool includeDefaultStateValues)
	{
		var output = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var property in properties)
		{
			if (!PropertyAliases.TryGetValue(property.Key, out var alias))
				continue;
			if (property.Value is null or PropertyExtractionError)
				continue;

			var value = CompactValue(property.Value);
			if (IsEmptyValue(value))
				continue;
			if (!includeDefaultStateValues && IsDefaultStateValue(alias, value))
				continue;
			if (IsNoisyPropertyValue(alias, value))
				continue;
			if (output.TryGetValue(alias, out var existing) && !IsEmptyValue(existing))
				continue;

			output[alias] = value;
		}

		return output;
	}

	private static Dictionary<string, object?> CompactMetadata(IReadOnlyDictionary<string, object?> metadata)
	{
		var output = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var item in metadata)
		{
			if (item.Value is null)
				continue;

			var value = CompactValue(item.Value);
			if (!IsEmptyValue(value))
				output[item.Key] = value;
		}

		return output;
	}

	private static object? CompactValue(object? value)
	{
		if (value is null or PropertyExtractionError)
			return null;
		if (value is string text)
			return string.IsNullOrWhiteSpace(text) ? null : Truncate(text.Trim(), 160);
		if (value is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
			return value;
		if (value is DateTime dateTime)
			return dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
		if (value is DateTimeOffset dateTimeOffset)
			return dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

		return Truncate(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, 160);
	}

	private static bool IsEmptyValue(object? value) =>
		value is null || value is string text && string.IsNullOrWhiteSpace(text);

	private static bool ValuesEqual(object? left, object? right) =>
		JToken.DeepEquals(ToToken(left), ToToken(right));

	private static JToken ToToken(object? value) =>
		value is null ? JValue.CreateNull() : JToken.FromObject(value);

	private static bool IsDefaultStateValue(string alias, object? value) =>
		((alias == "enabled" || alias == "visible") && value is true)
		|| (alias == "visibility" && value is string text && string.Equals(text, "Visible", StringComparison.Ordinal));

	private static bool IsNoisyPropertyValue(string alias, object? value) =>
		(alias == "content" || alias == "header")
		&& value is string text
		&& LooksLikeTypeName(text);

	private static bool LooksLikeTypeName(string value)
	{
		var segments = value.Split('.');
		if (segments.Length < 3)
			return false;
		return segments.All(IsTypeNameSegment);
	}

	private static bool IsTypeNameSegment(string segment)
	{
		if (string.IsNullOrWhiteSpace(segment))
			return false;
		if (!char.IsLetter(segment[0]) && segment[0] != '_')
			return false;
		for (var i = 1; i < segment.Length; i++)
		{
			var character = segment[i];
			if (!char.IsLetterOrDigit(character) && character != '_' && character != '+')
				return false;
		}

		return true;
	}

	private static string Truncate(string value, int maxLength) =>
		value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";

	private static void AddIfNotEmpty(IDictionary<string, object?> values, string key, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
			values[key] = value;
	}

	private static void AddIfNotNull(IDictionary<string, object?> values, string key, object? value)
	{
		if (value is not null)
			values[key] = value;
	}

	private sealed class CompactedNode
	{
		public CompactedNode(VisualTreeNodeDto source, Dictionary<string, object?> output)
		{
			Source = source;
			Output = output;
		}

		public VisualTreeNodeDto Source { get; }

		public Dictionary<string, object?> Output { get; }
	}
}
