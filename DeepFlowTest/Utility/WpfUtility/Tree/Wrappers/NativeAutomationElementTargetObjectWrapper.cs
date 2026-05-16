namespace DeepFlowTest.Utility.WpfUtility.Tree.Wrappers;

using System;
using System.Windows.Automation;

internal sealed class NativeAutomationElementTargetObjectWrapper : TargetObjectWrapper
{
	public NativeAutomationElementTargetObjectWrapper(AutomationElement target)
		: base(target, CreateMetadata(
			target,
			TargetObjectKind.NativeAutomationElement,
			"native-automation",
			canReceiveActions: true,
			TryGetNativeHwnd(target)))
	{
	}

	private static long? TryGetNativeHwnd(AutomationElement target)
	{
		try
		{
			var hwnd = target.Current.NativeWindowHandle;
			return hwnd == 0 ? null : hwnd;
		}
		catch (ElementNotAvailableException)
		{
			return null;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}
}
