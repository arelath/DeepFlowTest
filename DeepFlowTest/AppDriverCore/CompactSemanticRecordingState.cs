namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal sealed class CompactSemanticRecordingState
{
	private readonly Dictionary<string, VisualTreeNodeDto> nodesById = new(StringComparer.Ordinal);

	public bool TryGetPreviousNode(string targetId, out VisualTreeNodeDto node) =>
		nodesById.TryGetValue(targetId, out node!);

	public void Update(SemanticRecordingFrame frame)
	{
		_ = frame ?? throw new ArgumentNullException(nameof(frame));
		if (frame.Snapshot is not null)
		{
			nodesById.Clear();
			AddOrReplace(frame.Snapshot.Nodes);
			return;
		}

		if (frame.Delta is null)
			return;

		foreach (var targetId in frame.Delta.RemovedTargetIds)
			nodesById.Remove(targetId);
		AddOrReplace(frame.Delta.Added);
		AddOrReplace(frame.Delta.Changed);
	}

	private void AddOrReplace(IEnumerable<VisualTreeNodeDto> nodes)
	{
		foreach (var node in nodes)
		{
			if (!string.IsNullOrWhiteSpace(node.TargetId))
				nodesById[node.TargetId] = node;
		}
	}
}
