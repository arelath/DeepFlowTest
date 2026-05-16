namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System.Collections.Generic;
using System.Windows.Automation;
using static DeepFlowTest.Utility.WpfUtility.Tree.TreeService;

internal sealed class AutomationTreeTargetAdapter : TreeTargetAdapterBase
{
	public override bool CanHandle(object target, TargetObjectMetadata metadata) =>
		target is AutomationElement;

	public override IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		foreach (var child in EnumerateAutomationChildren((AutomationElement)target))
			yield return child;
	}
}
