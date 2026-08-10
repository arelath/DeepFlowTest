namespace DeepFlowTest;

using DeepFlowTest.Interop;

internal interface IElementContext
{
	VisualTreeSnapshot ResolveSnapshot(Element element, VisualTreeSnapshot? currentSnapshot);

	Element CreateElement(VisualTreeNodeDto node, VisualTreeSnapshot snapshot);

	void SetProperty(Element element, string propertyName, object? value);

	void OnTargetChanged(Element element, string previousTargetId, string replacementTargetId);
}
