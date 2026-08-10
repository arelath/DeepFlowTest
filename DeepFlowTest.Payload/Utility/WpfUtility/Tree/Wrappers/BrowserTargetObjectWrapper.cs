namespace DeepFlowTest.Utility.WpfUtility.Tree.Wrappers;

using System.Windows;

internal sealed class BrowserTargetObjectWrapper : TargetObjectWrapper
{
	public BrowserTargetObjectWrapper(object target)
		: base(target, CreateMetadata(target, TargetObjectKind.WebBrowser, "browser", canReceiveActions: target is UIElement))
	{
	}
}
