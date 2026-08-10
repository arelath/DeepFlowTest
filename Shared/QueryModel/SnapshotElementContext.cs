namespace DeepFlowTest;

using System;
using DeepFlowTest.Interop;

internal sealed class SnapshotElementContext : IElementContext
{
	public static SnapshotElementContext Instance { get; } = new();

	private SnapshotElementContext()
	{
	}

	public VisualTreeSnapshot ResolveSnapshot(Element element, VisualTreeSnapshot? currentSnapshot) =>
		currentSnapshot ?? throw new InvalidOperationException("A snapshot-backed element requires a visual-tree snapshot.");

	public Element CreateElement(VisualTreeNodeDto node, VisualTreeSnapshot snapshot) =>
		new(this, node, snapshot);

	public void SetProperty(Element element, string propertyName, object? value)
	{
		throw new InvalidOperationException("A snapshot-backed element cannot perform driver actions.");
	}

	public void OnTargetChanged(Element element, string previousTargetId, string replacementTargetId)
	{
	}
}
