namespace DeepFlowTest;

using System;
using DeepFlowTest.Interop;

internal sealed class ClientElementContext : IElementContext
{
	public ClientElementContext(AppDriver driver)
	{
		Driver = driver ?? throw new ArgumentNullException(nameof(driver));
	}

	public AppDriver Driver { get; }

	public VisualTreeSnapshot ResolveSnapshot(Element element, VisualTreeSnapshot? currentSnapshot) =>
		currentSnapshot ?? Driver.GetVisualTree();

	public Element CreateElement(VisualTreeNodeDto node, VisualTreeSnapshot snapshot) =>
		new(Driver, node, snapshot: snapshot);

	public void SetProperty(Element element, string propertyName, object? value)
	{
		element.SetProperty(propertyName, value);
	}

	public void OnTargetChanged(Element element, string previousTargetId, string replacementTargetId)
	{
		Driver.MoveElementRegistration(element, previousTargetId, replacementTargetId);
	}
}
