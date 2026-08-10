namespace DeepFlowTest.Interop;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

public sealed class VisualTreeSnapshotDelta
{
	public long BaseSequenceNumber { get; set; }

	public long CurrentSequenceNumber { get; set; }

	public List<VisualTreeNodeDto> Added { get; set; } = [];

	public List<string> RemovedTargetIds { get; set; } = [];

	public List<VisualTreeNodeDto> Changed { get; set; } = [];

	public bool IsDelta => true;

	public bool HasChanges => Added.Count != 0 || RemovedTargetIds.Count != 0 || Changed.Count != 0;

	public List<string> RequestedProperties { get; set; } = [];

	public Dictionary<string, object?> Metadata { get; set; } = [];

	public static VisualTreeSnapshotDelta FromSnapshots(VisualTreeSnapshot previous, VisualTreeSnapshot current) =>
		Create(previous, current);

	public static VisualTreeSnapshotDelta Create(VisualTreeSnapshot previous, VisualTreeSnapshot current)
	{
		_ = previous ?? throw new ArgumentNullException(nameof(previous));
		_ = current ?? throw new ArgumentNullException(nameof(current));

		var previousById = previous.Nodes.ToDictionary(static node => node.TargetId, StringComparer.Ordinal);
		var currentById = current.Nodes.ToDictionary(static node => node.TargetId, StringComparer.Ordinal);

		var added = current.Nodes.Where(node => !previousById.ContainsKey(node.TargetId)).ToList();
		var removedTargetIds = previous.Nodes.Where(node => !currentById.ContainsKey(node.TargetId)).Select(static node => node.TargetId).ToList();
		var changed = current.Nodes
			.Where(node => previousById.TryGetValue(node.TargetId, out var previousNode) && !NodesEqual(previousNode, node))
			.ToList();

		return new VisualTreeSnapshotDelta
		{
			BaseSequenceNumber = previous.SequenceNumber,
			CurrentSequenceNumber = current.SequenceNumber,
			Added = added,
			RemovedTargetIds = removedTargetIds,
			Changed = changed,
			RequestedProperties = current.RequestedPropertyNames.ToList(),
			Metadata = new Dictionary<string, object?>
			{
				["previousNodeCount"] = previous.Nodes.Count,
				["currentNodeCount"] = current.Nodes.Count,
				["addedCount"] = added.Count,
				["changedCount"] = changed.Count,
				["removedCount"] = removedTargetIds.Count,
				["generatedUtc"] = DateTimeOffset.UtcNow,
			},
		};
	}

	private static bool NodesEqual(VisualTreeNodeDto left, VisualTreeNodeDto right)
	{
		return string.Equals(left.TargetId, right.TargetId, StringComparison.Ordinal) &&
			string.Equals(left.ParentId, right.ParentId, StringComparison.Ordinal) &&
			left.IsRoot == right.IsRoot &&
			left.Depth == right.Depth &&
			left.SiblingIndex == right.SiblingIndex &&
			string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal) &&
			string.Equals(left.FrameworkTypeName, right.FrameworkTypeName, StringComparison.Ordinal) &&
			string.Equals(left.TargetKind, right.TargetKind, StringComparison.Ordinal) &&
			string.Equals(left.RuntimeFamily, right.RuntimeFamily, StringComparison.Ordinal) &&
			left.CanReceiveActions == right.CanReceiveActions &&
			left.Hwnd == right.Hwnd &&
			left.ChildIds.SequenceEqual(right.ChildIds, StringComparer.Ordinal) &&
			PropertiesEqual(left.Properties, right.Properties);
	}

	private static bool PropertiesEqual(IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right)
	{
		if (left.Count != right.Count)
			return false;

		foreach (var item in left)
		{
			if (!right.TryGetValue(item.Key, out var rightValue))
				return false;

			if (!JToken.DeepEquals(ToToken(item.Value), ToToken(rightValue)))
				return false;
		}

		return true;
	}

	private static JToken ToToken(object? value)
	{
		return value switch
		{
			null => JValue.CreateNull(),
			JToken token => token,
			_ => JToken.FromObject(value),
		};
	}
}
