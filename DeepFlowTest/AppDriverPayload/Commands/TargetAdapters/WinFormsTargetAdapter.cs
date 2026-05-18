namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;
using DeepFlowTest.Shared;
using Forms = System.Windows.Forms;

internal sealed class WinFormsTargetAdapter : UiTargetAdapterBase
{
	public override bool CanHandle(object target) =>
		target is Forms.Control;

	public override ActionResult Click(object target, MouseButtonKind button, int clickCount)
	{
		if (target is Forms.Button formsButton && button == MouseButtonKind.Left)
		{
			for (var i = 0; i < Math.Max(1, clickCount); i++)
				formsButton.PerformClick();
			return ActionResult.Ok();
		}

		return target is Forms.Control formsControl && NativeHwndTargetAdapter.TryClickNativeWindow(formsControl.Handle, button, clickCount)
			? ActionResult.Ok()
			: base.Click(target, button, clickCount);
	}

	public override ActionResult Focus(object target) =>
		target is Forms.Control control && control.Focus()
			? ActionResult.Ok()
			: ActionResult.Unsupported("Target cannot receive focus.");

	public override ActionResult TypeText(object target, string text, bool clearFirst)
	{
		if (target is Forms.TextBoxBase textBoxBase)
		{
			if (clearFirst)
				textBoxBase.Clear();
			textBoxBase.SelectedText = text;
			return ActionResult.Ok();
		}

		if (target is Forms.ComboBox formsComboBox)
		{
			formsComboBox.Text = clearFirst ? text : formsComboBox.Text + text;
			return ActionResult.Ok();
		}

		return base.TypeText(target, text, clearFirst);
	}

	public override ActionResult SendKeys(object target, object? keys, string keyText, int delayMs)
	{
		if (TryHandleFocusNavigation(target, keyText))
			return ActionResult.Ok();

		if (target is Forms.TextBoxBase formsTextBox)
		{
			if (TargetKeyboardInput.IsSelectAllShortcut(keyText))
				formsTextBox.SelectAll();
			else if (string.Equals(keyText, "Backspace", StringComparison.OrdinalIgnoreCase))
				DeleteTextBeforeCaret(formsTextBox);
			else if (string.Equals(keyText, "Delete", StringComparison.OrdinalIgnoreCase) || string.Equals(keyText, "Del", StringComparison.OrdinalIgnoreCase))
				DeleteTextAtCaret(formsTextBox);
			else if (string.Equals(keyText, "Space", StringComparison.OrdinalIgnoreCase))
				formsTextBox.SelectedText = " ";
			else if (TargetKeyboardInput.IsPlainTextInputKey(keyText))
				formsTextBox.SelectedText = keyText;
			else
				return TargetKeyboardInput.SendKeysToForeground(keys, delayMs);

			return ActionResult.Ok();
		}

		return target is Forms.Control
			? TargetKeyboardInput.SendKeysToForeground(keys, delayMs)
			: base.SendKeys(target, keys, keyText, delayMs);
	}

	public override bool TryEnsureForeground(object target)
	{
		if (target is not Forms.Control control)
			return base.TryEnsureForeground(target);

		var handle = control.FindForm()?.Handle ?? control.Handle;
		var foregroundSet = handle != IntPtr.Zero && NativeMethods.SetForegroundWindow(handle);
		var focusSet = Focus(target).Success;
		return foregroundSet || focusSet;
	}

	public override PointerTargetResult GetPointerTarget(object target, PointerAnchor anchor)
	{
		if (target is not Forms.Control control)
			return base.GetPointerTarget(target, anchor);

		if (!control.Visible)
			return PointerTargetResult.Unsupported("WinForms target is not visible.");
		if (!control.Enabled)
			return PointerTargetResult.Unsupported("WinForms target is not enabled.");
		if (control.ClientSize.Width <= 0 || control.ClientSize.Height <= 0)
			return PointerTargetResult.Unsupported("WinForms target has no renderable size.");

		var local = new System.Drawing.Point(
			(int)Math.Round(control.ClientSize.Width * anchor.X),
			(int)Math.Round(control.ClientSize.Height * anchor.Y));
		var screen = control.PointToScreen(local);
		var owner = control.FindForm()?.Handle ?? control.Handle;
		return PointerTargetResult.FromTarget(new PointerTarget(
			screen.X,
			screen.Y,
			owner,
			control.GetType().FullName ?? control.GetType().Name));
	}

	public override ActionResult RunKnownOperation(object target, string? operation)
	{
		switch (operation?.Trim())
		{
			case "Focus":
				return Focus(target);
			case "BringIntoView":
				if (target is Forms.Control formsBringIntoViewControl)
				{
					if (formsBringIntoViewControl.Parent is Forms.ScrollableControl scrollableControl)
						scrollableControl.ScrollControlIntoView(formsBringIntoViewControl);
					return ActionResult.Ok();
				}

				break;
			case "Select":
				if (target is Forms.Control formsSelectControl)
				{
					formsSelectControl.Select();
					return ActionResult.Ok();
				}

				break;
			case "Expand":
				if (target is Forms.ComboBox formsExpandComboBox)
				{
					formsExpandComboBox.DroppedDown = true;
					return ActionResult.Ok();
				}

				break;
			case "Collapse":
				if (target is Forms.ComboBox formsCollapseComboBox)
				{
					formsCollapseComboBox.DroppedDown = false;
					return ActionResult.Ok();
				}

				break;
			case "AcceptDialog":
			case "CancelDialog":
				if (target is Forms.Form form)
				{
					form.DialogResult = string.Equals(operation?.Trim(), "AcceptDialog", StringComparison.Ordinal)
						? Forms.DialogResult.OK
						: Forms.DialogResult.Cancel;
					form.Close();
					return ActionResult.Ok();
				}

				break;
		}

		return base.RunKnownOperation(target, operation);
	}

	private static void DeleteTextBeforeCaret(Forms.TextBoxBase textBox)
	{
		if (textBox.SelectionLength > 0)
		{
			var selectionStart = textBox.SelectionStart;
			textBox.SelectedText = string.Empty;
			textBox.SelectionStart = selectionStart;
			return;
		}

		if (textBox.SelectionStart <= 0 || textBox.Text.Length == 0)
			return;

		var removeIndex = textBox.SelectionStart - 1;
		textBox.Text = textBox.Text.Remove(removeIndex, 1);
		textBox.SelectionStart = removeIndex;
	}

	private static void DeleteTextAtCaret(Forms.TextBoxBase textBox)
	{
		if (textBox.SelectionLength > 0)
		{
			var selectionStart = textBox.SelectionStart;
			textBox.SelectedText = string.Empty;
			textBox.SelectionStart = selectionStart;
			return;
		}

		if (textBox.SelectionStart >= textBox.Text.Length)
			return;

		var removeIndex = textBox.SelectionStart;
		textBox.Text = textBox.Text.Remove(removeIndex, 1);
		textBox.SelectionStart = removeIndex;
	}

	private static bool TryHandleFocusNavigation(object target, string keyText)
	{
		if (target is not Forms.Control control || !TryGetTabDirection(keyText, out var forward))
			return false;

		var form = control.FindForm();
		if (form is null)
			return false;

		control.Focus();
		return form.SelectNextControl(control, forward, tabStopOnly: true, nested: true, wrap: true);
	}

	private static bool TryGetTabDirection(string keyText, out bool forward)
	{
		var normalized = keyText.Replace(" ", string.Empty);
		if (string.Equals(normalized, "Tab", StringComparison.OrdinalIgnoreCase))
		{
			forward = true;
			return true;
		}

		if (string.Equals(normalized, "Shift+Tab", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(normalized, "LeftShift+Tab", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(normalized, "RightShift+Tab", StringComparison.OrdinalIgnoreCase))
		{
			forward = false;
			return true;
		}

		forward = true;
		return false;
	}
}
