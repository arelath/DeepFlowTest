namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Automation;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;
using DeepFlowTest.Shared;

internal sealed class NativeHwndTargetAdapter : UiTargetAdapterBase
{
	public override bool CanHandle(object target) =>
		target is IntPtr;

	public override ActionResult Click(object target, MouseButtonKind button, int clickCount) =>
		target is IntPtr hwnd && TryClickNativeWindow(hwnd, button, clickCount)
			? ActionResult.Ok()
			: base.Click(target, button, clickCount);

	public override ActionResult Focus(object target) =>
		target is IntPtr hwnd && hwnd != IntPtr.Zero && NativeMethods.SetForegroundWindow(hwnd)
			? ActionResult.Ok()
			: ActionResult.Unsupported("Target cannot receive focus.");

	public override ActionResult TypeText(object target, string text, bool clearFirst) =>
		target is IntPtr hwnd && TrySetNativeWindowText(hwnd, text, clearFirst)
			? ActionResult.Ok()
			: base.TypeText(target, text, clearFirst);

	public override ActionResult SendKeys(object target, object? keys, string keyText, int delayMs) =>
		TargetKeyboardInput.SendKeysToForeground(keys, delayMs);

	public override bool TryEnsureForeground(object target) =>
		Focus(target).Success;

	public override PointerTargetResult GetPointerTarget(object target, PointerAnchor anchor)
	{
		if (target is not IntPtr hwnd)
			return base.GetPointerTarget(target, anchor);
		if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
			return PointerTargetResult.Unsupported("Native HWND target is not a valid window.");
		if (!NativeMethods.IsWindowVisible(hwnd))
			return PointerTargetResult.Unsupported("Native HWND target is not visible.");
		if (!NativeMethods.IsWindowEnabled(hwnd))
			return PointerTargetResult.Unsupported("Native HWND target is not enabled.");
		if (!NativeMethods.GetClientRect(hwnd, out var rect))
			return PointerTargetResult.Unsupported("Native HWND client rectangle could not be read.");

		var width = rect.Right - rect.Left;
		var height = rect.Bottom - rect.Top;
		if (width <= 0 || height <= 0)
			return PointerTargetResult.Unsupported("Native HWND target has no renderable size.");

		var point = new NativeMethods.NativePoint
		{
			X = (int)Math.Round(width * anchor.X),
			Y = (int)Math.Round(height * anchor.Y),
		};
		if (!NativeMethods.ClientToScreen(hwnd, ref point))
			return PointerTargetResult.Unsupported("Native HWND client coordinates could not be converted to screen coordinates.");

		return PointerTargetResult.FromTarget(new PointerTarget(point.X, point.Y, hwnd, "HWND"));
	}

	public override ActionResult SetProperty(object target, string propertyName, object? value)
	{
		if (IsNativeTextProperty(propertyName))
		{
			var textValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
			if (target is IntPtr hwnd && TrySetNativeWindowText(hwnd, textValue, clearFirst: true))
				return ActionResult.Ok();
		}

		return ActionResult.Unsupported($"Property '{propertyName}' was not found.");
	}

	public override ActionResult RunKnownOperation(object target, string? operation)
	{
		switch (operation?.Trim())
		{
			case "Focus":
				return Focus(target);
			case "AcceptDialog":
			case "CancelDialog":
				var accept = string.Equals(operation?.Trim(), "AcceptDialog", StringComparison.Ordinal);
				if (target is IntPtr hwnd && TryInvokeNativeDialogButton(hwnd, accept))
					return ActionResult.Ok();
				break;
		}

		return base.RunKnownOperation(target, operation);
	}

	internal static bool TryClickNativeWindow(IntPtr hwnd, MouseButtonKind mouseButton, int clickCount)
	{
		if (hwnd == IntPtr.Zero || !NativeMethods.GetClientRect(hwnd, out var rect))
			return false;

		var x = Math.Max(0, (rect.Right - rect.Left) / 2);
		var y = Math.Max(0, (rect.Bottom - rect.Top) / 2);
		var lParam = new IntPtr((y << 16) | (x & 0xffff));
		var downMessage = mouseButton switch
		{
			MouseButtonKind.Right => NativeMethods.WM_RBUTTONDOWN,
			MouseButtonKind.Middle => NativeMethods.WM_MBUTTONDOWN,
			_ => NativeMethods.WM_LBUTTONDOWN,
		};
		var upMessage = mouseButton switch
		{
			MouseButtonKind.Right => NativeMethods.WM_RBUTTONUP,
			MouseButtonKind.Middle => NativeMethods.WM_MBUTTONUP,
			_ => NativeMethods.WM_LBUTTONUP,
		};
		var count = Math.Max(1, clickCount);
		for (var i = 0; i < count; i++)
		{
			if (mouseButton == MouseButtonKind.Left)
				NativeMethods.SendMessage(hwnd, NativeMethods.BM_CLICK, IntPtr.Zero, IntPtr.Zero);

			if (!NativeMethods.PostMessage(hwnd, downMessage, IntPtr.Zero, lParam) ||
				!NativeMethods.PostMessage(hwnd, upMessage, IntPtr.Zero, lParam))
			{
				return false;
			}
		}

		return true;
	}

	private static bool TrySetNativeWindowText(IntPtr hwnd, string text, bool clearFirst)
	{
		if (hwnd == IntPtr.Zero)
			return false;

		var value = text;
		if (!clearFirst)
		{
			var length = NativeMethods.GetWindowTextLength(hwnd);
			if (length > 0)
			{
				var builder = new StringBuilder(length + 1);
				NativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
				value = builder + text;
			}
		}

		return NativeMethods.SendMessage(hwnd, NativeMethods.WM_SETTEXT, IntPtr.Zero, value) != IntPtr.Zero;
	}

	private static bool TryInvokeNativeDialogButton(IntPtr hwnd, bool accept)
	{
		if (hwnd == IntPtr.Zero)
			return false;

		var automationId = accept ? "1" : "2";
		try
		{
			var root = AutomationElement.FromHandle(hwnd);
			if (root is not null)
			{
				var button = root.FindFirst(
					TreeScope.Descendants,
					new AndCondition(
						new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
						new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)));
				if (button is not null && AutomationTargetAdapter.TryInvokeAutomation(button))
					return true;
			}
		}
		catch (ElementNotAvailableException)
		{
		}
		catch (InvalidOperationException)
		{
		}

		var controlId = accept ? 1 : 2;
		foreach (var child in EnumerateNativeChildWindows(hwnd))
		{
			if (NativeMethods.GetDlgCtrlID(child) == controlId && TryClickNativeWindow(child, MouseButtonKind.Left, 1))
				return true;
		}

		return false;
	}

	private static IEnumerable<IntPtr> EnumerateNativeChildWindows(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero)
			yield break;

		List<IntPtr> children = [];
		NativeMethods.EnumChildWindows(hwnd, (child, _) =>
		{
			children.Add(child);
			return true;
		}, IntPtr.Zero);

		foreach (var child in children)
			yield return child;
	}

	private static bool IsNativeTextProperty(string propertyName) =>
		string.Equals(propertyName, KnownProperties.FileName, StringComparison.Ordinal)
		|| string.Equals(propertyName, KnownProperties.Text, StringComparison.Ordinal)
		|| string.Equals(propertyName, KnownProperties.Value, StringComparison.Ordinal);
}
