namespace DeepFlowTest.Interop;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public sealed class VisualTreeSnapshotDelta
{
	public long BaseSequenceNumber { get; set; }

	public long CurrentSequenceNumber { get; set; }

	public List<VisualTreeNodeDto> Added { get; set; } = new();

	public List<string> RemovedTargetIds { get; set; } = new();

	public List<VisualTreeNodeDto> Changed { get; set; } = new();

	public bool HasChanges => Added.Count != 0 || RemovedTargetIds.Count != 0 || Changed.Count != 0;

	public static VisualTreeSnapshotDelta Create(VisualTreeSnapshot previous, VisualTreeSnapshot current)
	{
		_ = previous ?? throw new ArgumentNullException(nameof(previous));
		_ = current ?? throw new ArgumentNullException(nameof(current));

		var previousById = previous.Nodes.ToDictionary(static node => node.TargetId, StringComparer.Ordinal);
		var currentById = current.Nodes.ToDictionary(static node => node.TargetId, StringComparer.Ordinal);

		return new VisualTreeSnapshotDelta
		{
			BaseSequenceNumber = previous.SequenceNumber,
			CurrentSequenceNumber = current.SequenceNumber,
			Added = current.Nodes.Where(node => !previousById.ContainsKey(node.TargetId)).ToList(),
			RemovedTargetIds = previous.Nodes.Where(node => !currentById.ContainsKey(node.TargetId)).Select(static node => node.TargetId).ToList(),
			Changed = current.Nodes
				.Where(node => previousById.TryGetValue(node.TargetId, out var previousNode) && !NodesEqual(previousNode, node))
				.ToList(),
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
			left.Hwnd == right.Hwnd &&
			left.ChildIds.SequenceEqual(right.ChildIds, StringComparer.Ordinal) &&
			string.Equals(JsonConvert.SerializeObject(left.Properties), JsonConvert.SerializeObject(right.Properties), StringComparison.Ordinal);
	}
}
