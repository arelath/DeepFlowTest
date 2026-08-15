namespace DeepFlowTest;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using Newtonsoft.Json;

public sealed class SemanticRecordingTreeProjector
{
	private static readonly string[] IdentityProperties =
	[
		"automationId",
		"name",
		"automationName",
		"text",
		"content",
		"header",
		"title",
		"uid",
		"source",
	];

	private static readonly string[] StateProperties =
	[
		"root",
		"visible",
		"enabled",
		"checked",
		"expanded",
		"open",
		"selected",
		"submenuOpen",
		"visibility",
	];

	private static readonly HashSet<string> StatePropertySet = new(StateProperties, StringComparer.Ordinal);
	private static readonly IReadOnlyDictionary<string, object?> EmptyProperties = new Dictionary<string, object?>(StringComparer.Ordinal);

	private readonly SemanticRecordingFormattingOptions options;
	private readonly Dictionary<string, VisualTreeNodeDto> nodesById = new(StringComparer.Ordinal);
	private readonly Dictionary<string, long> orderByTargetId = new(StringComparer.Ordinal);
	private long nextOrder;

	public SemanticRecordingTreeProjector()
		: this(null)
	{
	}

	public SemanticRecordingTreeProjector(SemanticRecordingFormattingOptions? options)
	{
		this.options = options ?? new SemanticRecordingFormattingOptions();
	}

	public SemanticRecordingTreeFrame Apply(SemanticRecordingFrame frame)
	{
		_ = frame ?? throw new ArgumentNullException(nameof(frame));
		if (frame.Snapshot is not null)
			return ApplySnapshot(frame);
		if (frame.Delta is not null)
			return ApplyDelta(frame);
		if (frame.Action is not null)
			return ApplyAction(frame);

		var projection = ProjectCurrentState();
		return CreateFrame(
			frame,
			projection,
			markers: [],
			actionTargetId: null,
			SummarizeFrameKind(frame.FrameKind));
	}

	private SemanticRecordingTreeFrame ApplySnapshot(SemanticRecordingFrame frame)
	{
		var snapshot = frame.Snapshot!;
		ReplaceState(snapshot.Nodes);
		var projection = ProjectCurrentState();
		var summary = $"nodes {projection.NodeCount.ToString(CultureInfo.InvariantCulture)}/{snapshot.Nodes.Count.ToString(CultureInfo.InvariantCulture)}";
		if (snapshot.IsTruncated)
			summary += " truncated";

		return CreateFrame(frame, projection, markers: [], actionTargetId: null, summary);
	}

	private SemanticRecordingTreeFrame ApplyAction(SemanticRecordingFrame frame)
	{
		var action = frame.Action!;
		var projection = ProjectCurrentState();
		var actionTargetId = string.IsNullOrWhiteSpace(action.Target.TargetId) ? null : action.Target.TargetId;
		if (actionTargetId is not null && projection.NodesById.TryGetValue(actionTargetId, out var target))
			target.IsActionTarget = true;

		return CreateFrame(frame, projection, markers: [], actionTargetId, SummarizeAction(action));
	}

	private SemanticRecordingTreeFrame ApplyDelta(SemanticRecordingFrame frame)
	{
		var delta = frame.Delta!;
		var previousNodesById = new Dictionary<string, VisualTreeNodeDto>(nodesById, StringComparer.Ordinal);
		var previousProjection = ProjectCurrentState();
		var addedIds = new HashSet<string>(delta.Added.Select(static node => node.TargetId).Where(static id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
		var changedIds = new HashSet<string>(delta.Changed.Select(static node => node.TargetId).Where(static id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
		var removedIds = new HashSet<string>(delta.RemovedTargetIds.Where(static id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);

		AddOrReplace(delta.Added);
		AddOrReplace(delta.Changed);
		foreach (var targetId in removedIds)
		{
			nodesById.Remove(targetId);
			orderByTargetId.Remove(targetId);
		}

		var projection = ProjectCurrentState();
		var markers = new List<SemanticRecordingChangeMarker>();
		foreach (var targetId in addedIds)
		{
			if (!removedIds.Contains(targetId) && projection.NodesById.TryGetValue(targetId, out var node))
			{
				node.SetChange(SemanticRecordingChangeKind.Added, EmptyProperties);
				markers.Add(CreateMarker(node, SemanticRecordingChangeKind.Added, EmptyProperties));
			}
		}

		foreach (var targetId in changedIds)
		{
			if (addedIds.Contains(targetId) || removedIds.Contains(targetId))
				continue;
			if (!projection.NodesById.TryGetValue(targetId, out var node))
				continue;

			var changes = CreateChangedProperties(previousNodesById.TryGetValue(targetId, out var previous) ? previous : null, node.Source);
			if (changes.Count == 0)
				continue;

			node.SetChange(SemanticRecordingChangeKind.Changed, changes);
			markers.Add(CreateMarker(node, SemanticRecordingChangeKind.Changed, changes));
		}

		foreach (var targetId in delta.RemovedTargetIds)
		{
			if (string.IsNullOrWhiteSpace(targetId) || !previousProjection.NodesById.TryGetValue(targetId, out var previousNode))
				continue;

			var ghost = previousNode.CloneAsGhost();
			ghost.SetChange(SemanticRecordingChangeKind.Removed, EmptyProperties);
			InsertRemovedGhost(projection, previousProjection, targetId, ghost);
			markers.Add(CreateMarker(ghost, SemanticRecordingChangeKind.Removed, EmptyProperties));
		}

		var addedMarkerCount = markers.Count(static marker => marker.Kind == SemanticRecordingChangeKind.Added);
		var changedMarkerCount = markers.Count(static marker => marker.Kind == SemanticRecordingChangeKind.Changed);
		var removedMarkerCount = markers.Count(static marker => marker.Kind == SemanticRecordingChangeKind.Removed);
		var summary = $"+{addedMarkerCount.ToString(CultureInfo.InvariantCulture)} *{changedMarkerCount.ToString(CultureInfo.InvariantCulture)} -{removedMarkerCount.ToString(CultureInfo.InvariantCulture)}";
		return CreateFrame(frame, projection, markers, actionTargetId: null, summary);
	}

	private void InsertRemovedGhost(
		ProjectionResult currentProjection,
		ProjectionResult previousProjection,
		string removedTargetId,
		ProjectedTreeNodeBuilder ghost)
	{
		var parentId = previousProjection.ParentTargetIds.TryGetValue(removedTargetId, out var previousParentId)
			? previousParentId
			: null;
		while (!string.IsNullOrWhiteSpace(parentId) && !currentProjection.NodesById.ContainsKey(parentId!))
		{
			parentId = previousProjection.ParentTargetIds.TryGetValue(parentId!, out var nextParentId)
				? nextParentId
				: null;
		}

		if (!string.IsNullOrWhiteSpace(parentId) && currentProjection.NodesById.TryGetValue(parentId!, out var parent))
			parent.Children.Add(ghost);
		else
			currentProjection.Roots.Add(ghost);

		currentProjection.NodesById[ghost.TargetId] = ghost;
		currentProjection.ParentTargetIds[ghost.TargetId] = parentId;
	}

	private ProjectionResult ProjectCurrentState()
	{
		var orderedNodes = nodesById.Values
			.OrderBy(GetNodeOrder)
			.ThenBy(static node => node.Depth)
			.ThenBy(static node => node.SiblingIndex)
			.ToArray();
		var sourceById = orderedNodes
			.Where(static node => !string.IsNullOrWhiteSpace(node.TargetId))
			.ToDictionary(static node => node.TargetId, StringComparer.Ordinal);
		var included = new List<ProjectedTreeNodeBuilder>();
		foreach (var node in orderedNodes)
		{
			var properties = CompactSemanticRecordingFrame.CompactProperties(node.Properties);
			if (!CompactSemanticRecordingFrame.ShouldIncludeNode(node, properties, options))
				continue;

			included.Add(new ProjectedTreeNodeBuilder(
				node,
				GetShortId(node.TargetId),
				FormatNodeLabel(node.TypeName, node.TargetId, properties),
				properties));
		}

		var includedById = included
			.Where(static node => !string.IsNullOrWhiteSpace(node.TargetId))
			.ToDictionary(static node => node.TargetId, StringComparer.Ordinal);
		var roots = new List<ProjectedTreeNodeBuilder>();
		var parentTargetIds = new Dictionary<string, string?>(StringComparer.Ordinal);
		foreach (var node in included)
		{
			var parent = FindNearestIncludedParent(node.Source, sourceById, includedById);
			if (parent is null)
			{
				roots.Add(node);
				parentTargetIds[node.TargetId] = null;
			}
			else
			{
				parent.Children.Add(node);
				parentTargetIds[node.TargetId] = parent.TargetId;
			}
		}

		return new ProjectionResult(roots, includedById, parentTargetIds);
	}

	private long GetNodeOrder(VisualTreeNodeDto node) =>
		!string.IsNullOrWhiteSpace(node.TargetId) && orderByTargetId.TryGetValue(node.TargetId, out var order)
			? order
			: long.MaxValue;

	private void ReplaceState(IEnumerable<VisualTreeNodeDto> nodes)
	{
		nodesById.Clear();
		orderByTargetId.Clear();
		nextOrder = 0;
		AddOrReplace(nodes);
	}

	private void AddOrReplace(IEnumerable<VisualTreeNodeDto> nodes)
	{
		foreach (var node in nodes)
		{
			if (string.IsNullOrWhiteSpace(node.TargetId))
				continue;

			nodesById[node.TargetId] = node;
			if (!orderByTargetId.ContainsKey(node.TargetId))
				orderByTargetId[node.TargetId] = nextOrder++;
		}
	}

	private static ProjectedTreeNodeBuilder? FindNearestIncludedParent(
		VisualTreeNodeDto node,
		IReadOnlyDictionary<string, VisualTreeNodeDto> sourceById,
		IReadOnlyDictionary<string, ProjectedTreeNodeBuilder> includedById)
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

	private static IReadOnlyDictionary<string, object?> CreateChangedProperties(VisualTreeNodeDto? previous, VisualTreeNodeDto current)
	{
		if (previous is null)
			return CompactSemanticRecordingFrame.CompactProperties(current.Properties, includeDefaultStateValues: true);

		var changes = CompactSemanticRecordingFrame.CompactPropertyChanges(previous.Properties, current.Properties);
		if (!string.Equals(previous.TypeName, current.TypeName, StringComparison.Ordinal))
			changes["type"] = current.TypeName;
		if (previous.IsRoot != current.IsRoot)
			changes["root"] = current.IsRoot;
		if (!string.Equals(previous.ParentId, current.ParentId, StringComparison.Ordinal))
			changes["parentId"] = current.ParentId;
		return changes;
	}

	private static SemanticRecordingTreeFrame CreateFrame(
		SemanticRecordingFrame source,
		ProjectionResult projection,
		IReadOnlyList<SemanticRecordingChangeMarker> markers,
		string? actionTargetId,
		string summary) =>
		new()
		{
			FrameSequenceNumber = source.SequenceNumber,
			FrameKind = source.FrameKind ?? string.Empty,
			Roots = projection.Roots.Select(static node => node.ToNode()).ToArray(),
			Markers = markers,
			Action = source.Action,
			ActionTargetId = actionTargetId,
			Summary = summary,
		};

	private static SemanticRecordingChangeMarker CreateMarker(
		ProjectedTreeNodeBuilder node,
		SemanticRecordingChangeKind kind,
		IReadOnlyDictionary<string, object?> changedProperties) =>
		new()
		{
			TargetId = node.TargetId,
			ShortId = node.ShortId,
			Kind = kind,
			Label = node.Label,
			ChangedProperties = CopyProperties(changedProperties),
		};

	private static string SummarizeFrameKind(string? frameKind) =>
		string.IsNullOrWhiteSpace(frameKind) ? "frame" : frameKind!;

	private static string SummarizeAction(RecordedInputAction action)
	{
		var kind = string.IsNullOrWhiteSpace(action.ActionKind) ? "action" : action.ActionKind;
		var target = FormatRecordedTarget(action.Target);
		var parts = new List<string> { kind };
		if (!string.IsNullOrWhiteSpace(target))
			parts.Add(target);
		if (action.WheelDelta.HasValue)
			parts.Add($"delta={action.WheelDelta.Value.ToString(CultureInfo.InvariantCulture)}");
		if (!string.IsNullOrWhiteSpace(action.Keys))
			parts.Add($"keys={FormatValue(action.Keys)}");
		else if (!string.IsNullOrWhiteSpace(action.Text))
			parts.Add($"text={FormatValue(action.Text)}");

		return string.Join(" ", parts);
	}

	private static string FormatRecordedTarget(RecordedTarget target)
	{
		if (string.IsNullOrWhiteSpace(target.TargetId) && string.IsNullOrWhiteSpace(target.TypeName))
			return target.Summary ?? string.Empty;

		var properties = CompactSemanticRecordingFrame.CompactProperties(target.Properties);
		var label = FormatNodeLabel(string.IsNullOrWhiteSpace(target.TypeName) ? "Target" : target.TypeName, target.TargetId, properties);
		return string.IsNullOrWhiteSpace(label) ? target.Summary ?? string.Empty : label;
	}

	private static string FormatNodeLabel(
		string typeName,
		string targetId,
		IReadOnlyDictionary<string, object?> properties)
	{
		var tokens = new List<string>
		{
			string.IsNullOrWhiteSpace(typeName) ? "Node" : typeName,
		};
		if (!string.IsNullOrWhiteSpace(targetId))
			tokens.Add($"[{GetShortId(targetId)}]");

		var excluded = new HashSet<string>(StringComparer.Ordinal);
		foreach (var key in IdentityProperties)
		{
			if (properties.TryGetValue(key, out var value))
			{
				tokens.Add(FormatIdentityToken(key, value));
				excluded.Add(key);
			}
		}

		foreach (var key in StateProperties)
		{
			if (properties.TryGetValue(key, out var value))
			{
				tokens.Add(FormatStateToken(key, value));
				excluded.Add(key);
			}
		}

		foreach (var item in properties.OrderBy(static item => item.Key, StringComparer.Ordinal))
		{
			if (excluded.Contains(item.Key)
				|| StatePropertySet.Contains(item.Key)
				|| IdentityProperties.Contains(item.Key, StringComparer.Ordinal))
			{
				continue;
			}

			tokens.Add($"{item.Key}={FormatValue(item.Value)}");
		}

		return string.Join(" ", tokens.Where(static token => !string.IsNullOrWhiteSpace(token)));
	}

	private static string FormatIdentityToken(string key, object? value)
	{
		if (value is string text && IsBareToken(text))
		{
			if (key == "automationId")
				return "#" + text;
			if (key == "name")
				return "." + text;
		}

		var outputKey = key == "automationName" ? "autoName" : key;
		return $"{outputKey}={FormatValue(value)}";
	}

	private static string FormatStateToken(string key, object? value)
	{
		if (value is bool boolValue)
			return boolValue ? key : "!" + key;
		return $"{key}={FormatValue(value)}";
	}

	private static string FormatValue(object? value)
	{
		if (value is null)
			return "null";
		if (value is string text)
			return JsonConvert.SerializeObject(text);
		if (value is bool boolValue)
			return boolValue ? "true" : "false";
		if (value is IFormattable formattable && value is not IEnumerable)
			return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;

		return JsonConvert.SerializeObject(value, Formatting.None);
	}

	public static string GetShortId(string targetId)
	{
		if (string.IsNullOrWhiteSpace(targetId))
			return string.Empty;

		var lastDash = targetId.LastIndexOf('-');
		return lastDash >= 0 && lastDash + 1 < targetId.Length
			? targetId.Substring(lastDash + 1)
			: targetId;
	}

	private static bool IsBareToken(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return false;

		foreach (var character in value)
		{
			if (!char.IsLetterOrDigit(character)
				&& character is not '_' and not '-' and not '.' and not ':' and not '/')
			{
				return false;
			}
		}

		return true;
	}

	private static Dictionary<string, object?> CopyProperties(IEnumerable<KeyValuePair<string, object?>> properties)
	{
		var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var item in properties)
			copy[item.Key] = item.Value;

		return copy;
	}

	private sealed class ProjectionResult
	{
		public ProjectionResult(
			List<ProjectedTreeNodeBuilder> roots,
			Dictionary<string, ProjectedTreeNodeBuilder> nodesById,
			Dictionary<string, string?> parentTargetIds)
		{
			Roots = roots;
			NodesById = nodesById;
			ParentTargetIds = parentTargetIds;
		}

		public List<ProjectedTreeNodeBuilder> Roots { get; }

		public Dictionary<string, ProjectedTreeNodeBuilder> NodesById { get; }

		public Dictionary<string, string?> ParentTargetIds { get; }

		public int NodeCount => NodesById.Count;
	}

	private sealed class ProjectedTreeNodeBuilder
	{
		private IReadOnlyDictionary<string, object?> changedProperties = new Dictionary<string, object?>(StringComparer.Ordinal);

		public ProjectedTreeNodeBuilder(
			VisualTreeNodeDto source,
			string shortId,
			string label,
			IReadOnlyDictionary<string, object?> properties)
		{
			Source = source;
			TargetId = source.TargetId;
			ShortId = shortId;
			TypeName = source.TypeName;
			Label = label;
			Properties = CopyProperties(properties);
		}

		private ProjectedTreeNodeBuilder(
			VisualTreeNodeDto source,
			string targetId,
			string shortId,
			string typeName,
			string label,
			IReadOnlyDictionary<string, object?> properties)
		{
			Source = source;
			TargetId = targetId;
			ShortId = shortId;
			TypeName = typeName;
			Label = label;
			Properties = CopyProperties(properties);
		}

		public VisualTreeNodeDto Source { get; }

		public string TargetId { get; }

		public string ShortId { get; }

		public string TypeName { get; }

		public string Label { get; }

		public IReadOnlyDictionary<string, object?> Properties { get; }

		public List<ProjectedTreeNodeBuilder> Children { get; } = [];

		public SemanticRecordingChangeKind ChangeKind { get; private set; }

		public bool IsActionTarget { get; set; }

		public IReadOnlyDictionary<string, object?> ChangedProperties => changedProperties;

		public void SetChange(
			SemanticRecordingChangeKind kind,
			IReadOnlyDictionary<string, object?> changedProperties)
		{
			if (GetPriority(kind) < GetPriority(ChangeKind))
				return;

			ChangeKind = kind;
			this.changedProperties = CopyProperties(changedProperties);
		}

		public ProjectedTreeNodeBuilder CloneAsGhost() =>
			new(Source, TargetId, ShortId, TypeName, Label, Properties);

		public SemanticRecordingTreeNode ToNode() =>
			new()
			{
				TargetId = TargetId,
				ShortId = ShortId,
				TypeName = TypeName,
				Label = Label,
				Properties = CopyProperties(Properties),
				Children = Children.Select(static child => child.ToNode()).ToArray(),
				ChangeKind = ChangeKind,
				ChangedProperties = CopyProperties(ChangedProperties),
				IsActionTarget = IsActionTarget,
			};

		private static int GetPriority(SemanticRecordingChangeKind kind) =>
			kind switch
			{
				SemanticRecordingChangeKind.Removed => 3,
				SemanticRecordingChangeKind.Added => 2,
				SemanticRecordingChangeKind.Changed => 1,
				_ => 0,
			};
	}
}

public sealed class SemanticRecordingTreeFrame
{
	public long FrameSequenceNumber { get; set; }

	public string FrameKind { get; set; } = string.Empty;

	public IReadOnlyList<SemanticRecordingTreeNode> Roots { get; set; } = [];

	public IReadOnlyList<SemanticRecordingChangeMarker> Markers { get; set; } = [];

	public RecordedInputAction? Action { get; set; }

	public string? ActionTargetId { get; set; }

	public string Summary { get; set; } = string.Empty;
}

public sealed class SemanticRecordingTreeNode
{
	public string TargetId { get; set; } = string.Empty;

	public string ShortId { get; set; } = string.Empty;

	public string TypeName { get; set; } = string.Empty;

	public string Label { get; set; } = string.Empty;

	public IReadOnlyDictionary<string, object?> Properties { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);

	public IReadOnlyList<SemanticRecordingTreeNode> Children { get; set; } = [];

	public SemanticRecordingChangeKind ChangeKind { get; set; }

	public IReadOnlyDictionary<string, object?> ChangedProperties { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);

	public bool IsActionTarget { get; set; }
}

public sealed class SemanticRecordingChangeMarker
{
	public string TargetId { get; set; } = string.Empty;

	public string ShortId { get; set; } = string.Empty;

	public SemanticRecordingChangeKind Kind { get; set; }

	public string Label { get; set; } = string.Empty;

	public IReadOnlyDictionary<string, object?> ChangedProperties { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}

public enum SemanticRecordingChangeKind
{
	None,
	Added,
	Changed,
	Removed,
}
