namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Utility;

internal static class TargetKeyboardInput
{
	public static bool IsPlainTextInputKey(string keyText) =>
		keyText.Length == 1 && !char.IsControl(keyText[0]);

	public static bool IsSelectAllShortcut(string keyText) =>
		string.Equals(keyText, "Control+A", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Ctrl+A", StringComparison.OrdinalIgnoreCase);

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
}
