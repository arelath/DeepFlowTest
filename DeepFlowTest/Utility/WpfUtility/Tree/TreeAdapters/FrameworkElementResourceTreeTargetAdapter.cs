namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System.Collections.Generic;
using System.Windows;

internal sealed class FrameworkElementResourceTreeTargetAdapter : TreeTargetAdapterBase
{
	public override bool CanHandle(object target, TargetObjectMetadata metadata) =>
		target is FrameworkElement { Resources.Count: > 0 };

	public override IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		yield return ((FrameworkElement)target).Resources;
	}
}
