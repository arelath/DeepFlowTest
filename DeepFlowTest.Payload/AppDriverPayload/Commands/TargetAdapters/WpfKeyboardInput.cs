namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Utility;

internal static class WpfKeyboardInput
{
	public static ActionResult TypeText(object target, string text, bool clearFirst, Func<ActionResult> fallback)
	{
		if (target is not UIElement uiElement)
			return fallback();

		uiElement.Focus();
		if (clearFirst)
			ClearText(target);
		KeyboardInput.TypeTextComposition(text);
		return ActionResult.Ok();
	}

	public static ActionResult SendKeys(object target, object? keys, int delayMs, Func<ActionResult> fallback)
	{
		if (target is not IInputElement inputElement)
			return fallback();

		WpfWindowActivation.Focus(target);
		WpfPointerInput.TryEnsureAppHooks();
		var groups = KeyboardInput.ParseKeyGroups(TargetValueConverter.UnwrapJsonValue(keys));
		return KeyboardInput.TryPressWpf(inputElement, groups, delayMs, out var error)
			? ActionResult.Ok()
			: ActionResult.Unsupported(error ?? "WPF key input could not be injected.");
	}

	public static object? GetFocusedTarget() =>
		Keyboard.FocusedElement;

	private static void ClearText(object target)
	{
		switch (target)
		{
			case TextBox textBox:
				textBox.Clear();
				break;
			case PasswordBox passwordBox:
				passwordBox.Clear();
				break;
			case TextBoxBase textBoxBase:
				textBoxBase.SelectAll();
				break;
			case ComboBox comboBox:
				comboBox.Text = string.Empty;
				break;
		}
	}
}
