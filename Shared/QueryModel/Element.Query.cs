namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DeepFlowTest.Interop;

public partial class Element
{
	private readonly IElementContext context;
	private VisualTreeNodeDto node;

	internal Element(IElementContext context, VisualTreeNodeDto node, VisualTreeSnapshot? snapshot = null)
	{
		this.context = context ?? throw new ArgumentNullException(nameof(context));
		this.node = node ?? throw new ArgumentNullException(nameof(node));
		Snapshot = snapshot;
	}

	protected Element(Element source)
	{
		_ = source ?? throw new ArgumentNullException(nameof(source));
		context = source.context;
		node = source.node;
		Snapshot = source.Snapshot;
		CopyRuntimeStateFrom(source);
	}

	public string TargetId => node.TargetId;

	public string TypeName => node.TypeName;

	public string? FrameworkTypeName => node.FrameworkTypeName;

	public IReadOnlyDictionary<string, object?> Properties => node.Properties;

	internal VisualTreeNodeDto SnapshotNode => node;

	internal VisualTreeSnapshot? CurrentSnapshot => Snapshot;

	protected VisualTreeSnapshot? Snapshot { get; private set; }

	internal IElementContext Context => context;

	public Element? Parent
	{
		get
		{
			if (Snapshot is null || node.ParentId is null)
				return null;

			var parent = Snapshot.Nodes.SingleOrDefault(candidate => candidate.TargetId == node.ParentId);
			return parent is null ? null : context.CreateElement(parent, Snapshot);
		}
	}

	public IReadOnlyList<Element> Children
	{
		get
		{
			var snapshot = context.ResolveSnapshot(this, Snapshot);
			Snapshot = snapshot;
			var byId = snapshot.Nodes.ToDictionary(static candidate => candidate.TargetId, StringComparer.Ordinal);
			if (byId.TryGetValue(node.TargetId, out var refreshedNode))
				node = refreshedNode;

			return node.ChildIds
				.Where(byId.ContainsKey)
				.Select(childId => context.CreateElement(byId[childId], snapshot))
				.ToArray();
		}
	}

	public IReadOnlyList<Element> Child => Children;

	public IReadOnlyList<Element> Descendants => Children.SelectMany(static child => new[] { child }.Concat(child.Descendants)).ToArray();

	public Element this[int childIndex] => Children[childIndex];

	public Primitive this[string propertyName]
	{
		get => Primitive.FromProperty(this, propertyName);
		set => context.SetProperty(this, propertyName, value?.Value);
	}

	public bool HasProperty(string propertyName) => Properties.ContainsKey(propertyName);

	public T? GetProperty<T>(string propertyName)
	{
		if (!Properties.TryGetValue(propertyName, out var value) || value is null)
			return default;

		if (value is T typed)
			return typed;

		return (T?)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
	}

	internal static Element FromSnapshot(VisualTreeNodeDto node, VisualTreeSnapshot snapshot) =>
		new(SnapshotElementContext.Instance, node, snapshot);

	protected void ReplaceNode(VisualTreeNodeDto replacement, VisualTreeSnapshot? snapshot = null)
	{
		_ = replacement ?? throw new ArgumentNullException(nameof(replacement));
		var previousTargetId = node.TargetId;
		node = replacement;
		Snapshot = snapshot;
		context.OnTargetChanged(this, previousTargetId, replacement.TargetId);
	}

	internal void ReplaceWith(Element replacement)
	{
		_ = replacement ?? throw new ArgumentNullException(nameof(replacement));
		ReplaceNode(replacement.node, replacement.Snapshot);
	}

	internal void RefreshFromCache(VisualTreeNodeDto replacement, VisualTreeSnapshot snapshot)
	{
		node = replacement;
		Snapshot = snapshot;
	}

	partial void CopyRuntimeStateFrom(Element source);
}
