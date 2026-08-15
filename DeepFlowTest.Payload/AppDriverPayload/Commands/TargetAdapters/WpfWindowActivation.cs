namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Shared;

internal static class WpfWindowActivation
{
	public static ActionResult Focus(object target) =>
		target is IInputElement inputElement && inputElement.Focus()
			? ActionResult.Ok()
			: ActionResult.Unsupported("Target cannot receive focus.");

	public static bool TryEnsureForeground(object target)
	{
		var foregroundSet = target switch
		{
			Window window => TrySetForegroundWindow(window),
			DependencyObject dependencyObject => TrySetForegroundWindow(Window.GetWindow(dependencyObject)),
			_ => false,
		};
		var focusSet = Focus(target).Success;
		return foregroundSet || focusSet;
	}

	public static IntPtr GetOwnerHwnd(UIElement element)
	{
		if (element is Window window)
			return new WindowInteropHelper(window).Handle;

		if (element is Visual visual && PresentationSource.FromVisual(visual) is HwndSource source)
			return source.Handle;

		var ownerWindow = Window.GetWindow(element);
		return ownerWindow is null ? IntPtr.Zero : new WindowInteropHelper(ownerWindow).Handle;
	}

	private static bool TrySetForegroundWindow(Window? window)
	{
		if (window is null)
			return false;

		var handle = new WindowInteropHelper(window).Handle;
		return handle != IntPtr.Zero && NativeMethods.SetForegroundWindow(handle);
	}
}
