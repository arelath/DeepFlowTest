namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Interop;

public sealed class NodeSnapshotOptions
{
	public string TargetId { get; set; } = string.Empty;

	public bool IncludeAncestors { get; set; }

	public bool IncludeChildren { get; set; }

	public bool IncludeSubtree { get; set; }

	public int SubtreeDepth { get; set; } = -1;

	public bool IncludePath { get; set; }

	public bool UseShortIds { get; set; } = true;

	public IReadOnlyList<string> Properties { get; set; } = Array.Empty<string>();
}

public sealed class NodeSnapshotService
{
	private readonly CliTargetIdService targetIds;
	private readonly TreeSnapshotService treeService;

	public NodeSnapshotService(CliTargetIdService? targetIds = null, TreeSnapshotService? treeService = null)
	{
		this.targetIds = targetIds ?? new CliTargetIdService();
		this.treeService = treeService ?? new TreeSnapshotService(this.targetIds);
	}

	public NodeResultData GetNode(VisualTreeSnapshot snapshot, NodeSnapshotOptions options)
	{
		var fullId = targetIds.Resolve(options.TargetId, snapshot);
		var relationships = SnapshotRelationships.Create(snapshot);
		var node = relationships.Nodes[fullId];
		var treeOptions = new TreeSnapshotOptions
		{
			IncludePath = options.IncludePath,
			IncludeTypeNames = true,
			UseShortIds = options.UseShortIds,
			Properties = options.Properties,
		};

		return new NodeResultData
		{
			Node = treeService.ShapeOne(node, snapshot, treeOptions),
			Ancestors = options.IncludeAncestors
				? relationships.AncestorsOf(fullId).Select(ancestor => treeService.ShapeOne(ancestor, snapshot, treeOptions)).ToList()
				: Array.Empty<TreeNodeData>(),
			Children = options.IncludeChildren
				? relationships.ChildrenOf(fullId).Select(child => treeService.ShapeOne(child, snapshot, treeOptions)).ToList()
				: Array.Empty<TreeNodeData>(),
			Subtree = options.IncludeSubtree
				? relationships.SubtreeOf(fullId, options.SubtreeDepth).Select(descendant => treeService.ShapeOne(descendant, snapshot, treeOptions)).ToList()
				: Array.Empty<TreeNodeData>(),
		};
	}

	public PropsResultData GetProps(VisualTreeSnapshot snapshot, NodeSnapshotOptions options)
	{
		var nodeResult = GetNode(snapshot, options);
		return new PropsResultData
		{
			TargetId = nodeResult.Node.TargetId,
			ShortId = nodeResult.Node.ShortId,
			TypeName = nodeResult.Node.TypeName,
			FrameworkTypeName = nodeResult.Node.FrameworkTypeName,
			Properties = nodeResult.Node.Properties,
		};
	}
}

public sealed class NodeResultData
{
	public TreeNodeData Node { get; set; } = new();

	public IReadOnlyList<TreeNodeData> Ancestors { get; set; } = Array.Empty<TreeNodeData>();

	public IReadOnlyList<TreeNodeData> Children { get; set; } = Array.Empty<TreeNodeData>();

	public IReadOnlyList<TreeNodeData> Subtree { get; set; } = Array.Empty<TreeNodeData>();
}

public sealed class PropsResultData
{
	public string TargetId { get; set; } = string.Empty;

	public string? ShortId { get; set; }

	public string? TypeName { get; set; }

	public string? FrameworkTypeName { get; set; }

	public Dictionary<string, object?> Properties { get; set; } = new(StringComparer.Ordinal);
}
