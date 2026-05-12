namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Interop;

public sealed class TreeSnapshotOptions
{
	public string Shape { get; set; } = "flat";

	public string? RootTargetId { get; set; }

	public int MaxDepth { get; set; } = -1;

	public int Limit { get; set; } = 1000;

	public bool IncludeHidden { get; set; }

	public bool IncludeTypeNames { get; set; }

	public bool IncludePath { get; set; }

	public bool UseShortIds { get; set; } = true;

	public IReadOnlyList<string> Properties { get; set; } = Array.Empty<string>();
}

public sealed class TreeSnapshotService
{
	private readonly CliTargetIdService targetIds;

	public TreeSnapshotService(CliTargetIdService? targetIds = null)
	{
		this.targetIds = targetIds ?? new CliTargetIdService();
	}

	public TreeSnapshotData Shape(VisualTreeSnapshot snapshot, TreeSnapshotOptions options)
	{
		_ = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
		_ = options ?? throw new ArgumentNullException(nameof(options));

		var relationships = SnapshotRelationships.Create(snapshot);
		var shape = NormalizeShape(options.Shape);
		var rootIds = ResolveRootIds(snapshot, options);
		var flattened = Flatten(snapshot, relationships, rootIds)
			.Where(item => options.MaxDepth < 0 || item.Depth <= options.MaxDepth)
			.Where(item => options.IncludeHidden || IsVisible(item.Node))
			.ToList();

		var truncated = snapshot.IsTruncated;
		var truncationReason = snapshot.TruncationReason;
		if (options.Limit > 0 && flattened.Count > options.Limit)
		{
			flattened = flattened.Take(options.Limit).ToList();
			truncated = true;
			truncationReason = "limit";
		}

		if (options.MaxDepth >= 0 && HasNodesBeyondDepth(snapshot, relationships, rootIds, options.MaxDepth))
		{
			truncated = true;
			truncationReason ??= "max-depth";
		}

		var nodeOutputs = flattened
			.Select(item => ToNodeOutput(item.Node, relationships, item.Depth, options))
			.ToList();

		return new TreeSnapshotData
		{
			Shape = shape,
			NodeCount = nodeOutputs.Count,
			TotalNodeCount = snapshot.NodeCount,
			Truncated = truncated,
			TruncationReason = truncationReason,
			RequestedProperties = options.Properties,
			Roots = shape == "nested"
				? BuildNested(nodeOutputs, rootIds)
				: nodeOutputs.Where(node => node.ParentId is null || rootIds.Contains(node.TargetId, StringComparer.Ordinal)).ToList(),
			Nodes = shape == "flat" ? nodeOutputs : Array.Empty<TreeNodeData>(),
		};
	}

	public TreeNodeData ShapeOne(VisualTreeNodeDto node, VisualTreeSnapshot snapshot, TreeSnapshotOptions options)
	{
		var relationships = SnapshotRelationships.Create(snapshot);
		return ToNodeOutput(node, relationships, relationships.DepthOf(node.TargetId), options);
	}

	private IReadOnlyList<string> ResolveRootIds(VisualTreeSnapshot snapshot, TreeSnapshotOptions options)
	{
		if (string.IsNullOrWhiteSpace(options.RootTargetId))
			return snapshot.RootIds.Count == 0
				? snapshot.Nodes.Where(static node => node.ParentId is null).Select(static node => node.TargetId).ToArray()
				: snapshot.RootIds;

		return new[] { targetIds.Resolve(options.RootTargetId!, snapshot) };
	}

	private static List<TreeNodeTraversalItem> Flatten(VisualTreeSnapshot snapshot, SnapshotRelationships relationships, IReadOnlyList<string> rootIds)
	{
		var result = new List<TreeNodeTraversalItem>();
		foreach (var rootId in rootIds)
			Visit(rootId, 0);

		return result;

		void Visit(string targetId, int depth)
		{
			if (!relationships.Nodes.TryGetValue(targetId, out var node))
				return;

			result.Add(new TreeNodeTraversalItem(node, depth));
			foreach (var childId in node.ChildIds)
				Visit(childId, depth + 1);
		}
	}

	private static bool HasNodesBeyondDepth(VisualTreeSnapshot snapshot, SnapshotRelationships relationships, IReadOnlyList<string> rootIds, int maxDepth)
	{
		return Flatten(snapshot, relationships, rootIds).Any(item => item.Depth > maxDepth);
	}

	private TreeNodeData ToNodeOutput(VisualTreeNodeDto node, SnapshotRelationships relationships, int depth, TreeSnapshotOptions options)
	{
		return new TreeNodeData
		{
			TargetId = node.TargetId,
			ShortId = options.UseShortIds ? targetIds.GetShortId(node.TargetId) : null,
			ParentId = node.ParentId,
			ChildIds = node.ChildIds,
			Depth = depth,
			SiblingIndex = relationships.SiblingIndexOf(node.TargetId),
			Path = options.IncludePath ? relationships.PathOf(node.TargetId) : null,
			TypeName = options.IncludeTypeNames ? node.TypeName : null,
			FrameworkTypeName = options.IncludeTypeNames ? node.FrameworkTypeName : null,
			Properties = SelectProperties(node, options.Properties),
		};
	}

	private static IReadOnlyList<TreeNodeData> BuildNested(IReadOnlyList<TreeNodeData> flatNodes, IReadOnlyList<string> rootIds)
	{
		var byId = flatNodes.ToDictionary(static node => node.TargetId, StringComparer.Ordinal);
		foreach (var node in flatNodes)
			node.Children = node.ChildIds.Where(byId.ContainsKey).Select(childId => byId[childId]).ToList();

		return rootIds.Where(byId.ContainsKey).Select(rootId => byId[rootId]).ToList();
	}

	private static Dictionary<string, object?> SelectProperties(VisualTreeNodeDto node, IReadOnlyList<string> properties)
	{
		if (properties.Count == 0)
			return node.Properties.ToDictionary(static property => property.Key, static property => NormalizeScalar(property.Value), StringComparer.Ordinal);

		var selected = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var property in properties)
		{
			if (node.Properties.TryGetValue(property, out var value))
				selected[property] = NormalizeScalar(value);
		}

		return selected;
	}

	private static object? NormalizeScalar(object? value)
	{
		return value switch
		{
			null => null,
			string or bool or int or long or double or float or decimal => value,
			_ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
		};
	}

	private static bool IsVisible(VisualTreeNodeDto node)
	{
		if (!node.Properties.TryGetValue("IsVisible", out var value) || value is null)
			return true;

		if (value is bool visible)
			return visible;

		return !string.Equals(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), "false", StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeShape(string shape)
	{
		if (string.Equals(shape, "nested", StringComparison.OrdinalIgnoreCase))
			return "nested";

		if (string.Equals(shape, "flat", StringComparison.OrdinalIgnoreCase))
			return "flat";

		throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported tree shape '{shape}'.");
	}

	private sealed record TreeNodeTraversalItem(VisualTreeNodeDto Node, int Depth);
}

public sealed class TreeSnapshotData
{
	public string Shape { get; set; } = "flat";

	public int NodeCount { get; set; }

	public int TotalNodeCount { get; set; }

	public bool Truncated { get; set; }

	public string? TruncationReason { get; set; }

	public IReadOnlyList<string> RequestedProperties { get; set; } = Array.Empty<string>();

	public IReadOnlyList<TreeNodeData> Roots { get; set; } = Array.Empty<TreeNodeData>();

	public IReadOnlyList<TreeNodeData> Nodes { get; set; } = Array.Empty<TreeNodeData>();
}

public sealed class TreeNodeData
{
	public string TargetId { get; set; } = string.Empty;

	public string? ShortId { get; set; }

	public string? ParentId { get; set; }

	public IReadOnlyList<string> ChildIds { get; set; } = Array.Empty<string>();

	public int Depth { get; set; }

	public int SiblingIndex { get; set; }

	public string? Path { get; set; }

	public string? TypeName { get; set; }

	public string? FrameworkTypeName { get; set; }

	public Dictionary<string, object?> Properties { get; set; } = new(StringComparer.Ordinal);

	public IReadOnlyList<TreeNodeData> Children { get; set; } = Array.Empty<TreeNodeData>();
}

internal sealed class SnapshotRelationships
{
	private readonly Dictionary<string, string> pathCache = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> depthCache = new(StringComparer.Ordinal);

	private SnapshotRelationships(Dictionary<string, VisualTreeNodeDto> nodes)
	{
		Nodes = nodes;
	}

	public Dictionary<string, VisualTreeNodeDto> Nodes { get; }

	public static SnapshotRelationships Create(VisualTreeSnapshot snapshot) =>
		new(snapshot.Nodes.ToDictionary(static node => node.TargetId, StringComparer.Ordinal));

	public int DepthOf(string targetId)
	{
		if (depthCache.TryGetValue(targetId, out var cached))
			return cached;

		if (!Nodes.TryGetValue(targetId, out var node) || string.IsNullOrWhiteSpace(node.ParentId))
			return depthCache[targetId] = 0;

		return depthCache[targetId] = DepthOf(node.ParentId!) + 1;
	}

	public int SiblingIndexOf(string targetId)
	{
		if (!Nodes.TryGetValue(targetId, out var node) || string.IsNullOrWhiteSpace(node.ParentId))
			return 0;

		return Nodes.TryGetValue(node.ParentId!, out var parent)
			? Math.Max(0, parent.ChildIds.IndexOf(targetId))
			: node.SiblingIndex;
	}

	public string PathOf(string targetId)
	{
		if (pathCache.TryGetValue(targetId, out var cached))
			return cached;

		if (!Nodes.TryGetValue(targetId, out var node) || string.IsNullOrWhiteSpace(node.ParentId))
			return pathCache[targetId] = "/" + targetId;

		return pathCache[targetId] = PathOf(node.ParentId!) + "/" + targetId;
	}

	public IEnumerable<VisualTreeNodeDto> AncestorsOf(string targetId)
	{
		var current = targetId;
		while (Nodes.TryGetValue(current, out var node) && !string.IsNullOrWhiteSpace(node.ParentId))
		{
			if (!Nodes.TryGetValue(node.ParentId!, out var parent))
				yield break;

			yield return parent;
			current = parent.TargetId;
		}
	}

	public IEnumerable<VisualTreeNodeDto> ChildrenOf(string targetId)
	{
		if (!Nodes.TryGetValue(targetId, out var node))
			yield break;

		foreach (var childId in node.ChildIds)
			if (Nodes.TryGetValue(childId, out var child))
				yield return child;
	}

	public IEnumerable<VisualTreeNodeDto> SubtreeOf(string targetId, int maxDepth)
	{
		if (maxDepth == 0)
			yield break;

		foreach (var child in ChildrenOf(targetId))
		{
			yield return child;
			if (maxDepth != 1)
			{
				foreach (var descendant in SubtreeOf(child.TargetId, maxDepth < 0 ? -1 : maxDepth - 1))
					yield return descendant;
			}
		}
	}
}
