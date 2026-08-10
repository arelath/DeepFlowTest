namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System.Collections.Generic;
using System.Windows.Controls;

internal sealed class ImageTreeTargetAdapter : TreeTargetAdapterBase
{
	public override bool CanHandle(object target, TargetObjectMetadata metadata) =>
		target is Image { Source: not null };

	public override IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		yield return ((Image)target).Source;
	}
}
