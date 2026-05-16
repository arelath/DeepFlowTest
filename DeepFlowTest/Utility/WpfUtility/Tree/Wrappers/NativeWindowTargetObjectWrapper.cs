namespace DeepFlowTest.Utility.WpfUtility.Tree.Wrappers;

using System;

internal sealed class NativeWindowTargetObjectWrapper : TargetObjectWrapper
{
	public NativeWindowTargetObjectWrapper(IntPtr target)
		: base(target, CreateMetadata(
			target,
			TargetObjectKind.NativeWindow,
			"native",
			canReceiveActions: target != IntPtr.Zero,
			target == IntPtr.Zero ? null : target.ToInt64(),
			displayTypeName: GetDisplayTypeName(target),
			targetObjectType: "HWND"))
	{
	}

	private static string GetDisplayTypeName(IntPtr target)
	{
		var className = NativeDialogService.GetClassName(target);
		return string.Equals(className, "#32770", StringComparison.Ordinal)
			? "Dialog"
			: "HWND";
	}
}
