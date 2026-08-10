namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System;
using System.Collections.Generic;
using static DeepFlowTest.Utility.WpfUtility.Tree.TreeService;

internal sealed class NativeHwndTreeTargetAdapter : TreeTargetAdapterBase
{
	public override bool CanHandle(object target, TargetObjectMetadata metadata) =>
		target is IntPtr;

	public override IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		var hwnd = (IntPtr)target;
		var automationElement = TryGetAutomationElement(hwnd);
		if (automationElement is not null)
			yield return automationElement;

		foreach (var childHwnd in EnumerateNativeChildWindows(hwnd))
			yield return childHwnd;
	}
}
