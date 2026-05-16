namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public partial class Element
{
	private readonly AppDriver? driver;
	private VisualTreeNodeDto node;

	internal Element(
		AppDriver driver,
		VisualTreeNodeDto node,
		ElementSelector? selector = null,
		VisualTreeSnapshot? snapshot = null,
		ElementRepairInfo? repairInfo = null,
		IReadOnlyList<ElementPathSegmentResponse>? diagnosticPath = null,
		bool register = true)
	{
		this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
		this.node = node ?? throw new ArgumentNullException(nameof(node));
		Selector = selector;
		Snapshot = snapshot;
		RepairInfo = repairInfo;
		DiagnosticPath = diagnosticPath ?? [];
		if (register)
			driver.RegisterElement(this);
	}

	internal Element(VisualTreeNodeDto node, VisualTreeSnapshot snapshot)
	{
		this.node = node ?? throw new ArgumentNullException(nameof(node));
		Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
		DiagnosticPath = [];
	}

	protected Element(Element source)
	{
		_ = source ?? throw new ArgumentNullException(nameof(source));
		driver = source.driver;
		node = source.node;
		Selector = source.Selector;
		Snapshot = source.Snapshot;
		RepairInfo = source.RepairInfo;
		DiagnosticPath = source.DiagnosticPath;
		driver?.RegisterElement(this);
	}

	public string TargetId => node.TargetId;

	public string TypeName => node.TypeName;

	public string? FrameworkTypeName => node.FrameworkTypeName;

	public IReadOnlyDictionary<string, object?> Properties => node.Properties;

	public ElementSelector? Selector { get; }

	internal ElementRepairInfo? RepairInfo { get; }

	internal VisualTreeNodeDto SnapshotNode => node;

	internal VisualTreeSnapshot? CurrentSnapshot => Snapshot;

	internal IReadOnlyList<ElementPathSegmentResponse> DiagnosticPath { get; }

	protected VisualTreeSnapshot? Snapshot { get; private set; }

	private AppDriver Driver =>
		driver ?? throw new InvalidOperationException("This element is only available while evaluating a target-side expression and cannot perform driver actions.");

	private ElementCommandExecutor Commands => Driver.ElementCommandExecutor;

	public Element? Parent
	{
		get
		{
			if (Snapshot is null || node.ParentId is null)
				return null;

			var parent = Snapshot.Nodes.SingleOrDefault(candidate => candidate.TargetId == node.ParentId);
			if (parent is null)
				return null;

			return driver is null
				? new Element(parent, Snapshot)
				: new Element(driver, parent, snapshot: Snapshot);
		}
	}

	public IReadOnlyList<Element> Children
	{
		get
		{
			var snapshot = Snapshot ?? Driver.GetVisualTree();
			Snapshot = snapshot;
			var byId = snapshot.Nodes.ToDictionary(static candidate => candidate.TargetId, StringComparer.Ordinal);
			if (byId.TryGetValue(node.TargetId, out var refreshedNode))
				node = refreshedNode;

			return node.ChildIds
				.Where(byId.ContainsKey)
				.Select(childId => driver is null
					? new Element(byId[childId], snapshot)
					: new Element(driver, byId[childId], snapshot: snapshot))
				.ToArray();
		}
	}

	public IReadOnlyList<Element> Child => Children;

	public IReadOnlyList<Element> Descendants => Children.SelectMany(static child => new[] { child }.Concat(child.Descendants)).ToArray();

	public Element this[int childIndex] => Children[childIndex];

	public Primitive this[string propertyName]
	{
		get => Primitive.FromProperty(this, propertyName);
		set => SetProperty(propertyName, value?.Value);
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

	internal static Element FromMatch(
		AppDriver driver,
		FindElementMatchResponse match,
		ElementSelector? selector,
		ElementRepairInfo? repairInfo = null)
	{
		return new Element(
			driver,
			new VisualTreeNodeDto
			{
				TargetId = match.TargetId,
				TypeName = match.TypeName,
				FrameworkTypeName = match.FrameworkTypeName,
				Properties = match.Properties,
			},
			selector,
			repairInfo: repairInfo,
			diagnosticPath: match.Path);
	}

	internal static Element FromNode(
		AppDriver driver,
		VisualTreeNodeDto node,
		VisualTreeSnapshot snapshot,
		ElementRepairInfo? repairInfo = null,
		bool register = true) =>
		new(driver, node, snapshot: snapshot, repairInfo: repairInfo, register: register);

	internal static Element FromSnapshot(VisualTreeNodeDto node, VisualTreeSnapshot snapshot) =>
		new(node, snapshot);

	protected void ReplaceNode(VisualTreeNodeDto replacement, VisualTreeSnapshot? snapshot = null)
	{
		var previousTargetId = node.TargetId;
		node = replacement;
		Snapshot = snapshot;
		Driver.MoveElementRegistration(this, previousTargetId, replacement.TargetId);
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

}
