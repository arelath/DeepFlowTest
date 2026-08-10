namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System.Collections.Generic;

internal sealed class SystemResourceTreeTargetAdapter : TreeTargetAdapterBase
{
	public override bool CanHandle(object target, TargetObjectMetadata metadata) =>
		target is SystemResourceRoot;

	public override IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		yield return ((SystemResourceRoot)target).Resources;
	}
}
