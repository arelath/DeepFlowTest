namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using static DeepFlowTest.Utility.WpfUtility.Tree.TreeService;

internal sealed class WpfTreeTargetAdapter : TreeTargetAdapterBase
{
	public override bool CanHandle(object target, TargetObjectMetadata metadata) =>
		target is DependencyObject;

	public override IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		var dependencyObject = (DependencyObject)target;
		foreach (var visualChild in EnumerateVisualChildren(dependencyObject))
			yield return visualChild;

		foreach (var logicalChild in EnumerateLogicalChildren(dependencyObject))
			yield return logicalChild;

		if (target is Popup { Child: { } popupChild })
			yield return popupChild;

		if (target is Window or HwndHost)
			foreach (var childHwnd in EnumerateNativeChildrenFromMetadata(metadata))
				yield return childHwnd;
	}

	public override bool TryGetIsVisible(object target, out bool isVisible)
	{
		if (target is UIElement element)
		{
			isVisible = element.Visibility == Visibility.Visible;
			return true;
		}

		return base.TryGetIsVisible(target, out isVisible);
	}
}
