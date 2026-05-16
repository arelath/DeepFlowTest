namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System.Collections.Generic;
using static DeepFlowTest.Utility.WpfUtility.Tree.TreeService;

internal sealed class HybridBridgeTreeTargetAdapter : TreeTargetAdapterBase
{
	public override bool StopsChildEnumeration => true;

	public override bool CanHandle(object target, TargetObjectMetadata metadata) =>
		TryGetHybridBridgeChild(target, out _);

	public override IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		if (TryGetHybridBridgeChild(target, out var child))
			yield return child;
	}
}
