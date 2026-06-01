namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Utility;

internal static class TargetKeyboardInput
{
	public static void TypePhysical(string text) =>
		KeyboardInput.TypePhysical(text);

	public static ActionResult SendKeysToForeground(object? keys, int delayMs)
	{
		var groups = KeyboardInput.ParseKeyGroups(TargetValueConverter.UnwrapJsonValue(keys));
		if (groups.Count == 0)
			return ActionResult.Unsupported("Key input cannot be empty.");

		foreach (var group in groups)
			KeyboardInput.PressPhysical(group, delayMs);

		return ActionResult.Ok();
	}

	public static ActionResult SendKeysToHwnd(IntPtr hwnd, object? keys, int delayMs)
	{
		var groups = KeyboardInput.ParseKeyGroups(TargetValueConverter.UnwrapJsonValue(keys));
		if (groups.Count == 0)
			return ActionResult.Unsupported("Key input cannot be empty.");
		if (hwnd == IntPtr.Zero)
			return ActionResult.Unsupported("Target window handle is invalid.");

		return KeyboardInput.TryPressNativeHwnd(hwnd, groups, delayMs)
			? ActionResult.Ok()
			: ActionResult.Unsupported("Native key input could not be posted to the target window.");
	}
}
