namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Globalization;
using System.Windows.Automation;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;

internal sealed class AutomationTargetAdapter : UiTargetAdapterBase
{
	public override bool CanHandle(object target) =>
		target is AutomationElement;

	public override ActionResult Click(object target, MouseButtonKind button, int clickCount) =>
		target is AutomationElement automationElement && button == MouseButtonKind.Left && TryInvokeAutomation(automationElement, clickCount)
			? ActionResult.Ok()
			: base.Click(target, button, clickCount);

	public override ActionResult Focus(object target)
	{
		if (target is not AutomationElement automationElement)
			return base.Focus(target);

		automationElement.SetFocus();
		return ActionResult.Ok();
	}

	public override ActionResult TypeText(object target, string text, bool clearFirst) =>
		target is AutomationElement automationElement && TrySetAutomationValue(automationElement, text)
			? ActionResult.Ok()
			: base.TypeText(target, text, clearFirst);

	public override ActionResult SendKeys(object target, object? keys, string keyText, int delayMs) =>
		TargetKeyboardInput.SendKeysToForeground(keys, delayMs);

	public override ActionResult SetProperty(object target, string propertyName, object? value)
	{
		if (IsNativeTextProperty(propertyName))
		{
			var textValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
			if (target is AutomationElement automationElement && TrySetAutomationValue(automationElement, textValue))
				return ActionResult.Ok();
		}

		return base.SetProperty(target, propertyName, value);
	}

	public override ActionResult RunKnownOperation(object target, string? operation)
	{
		if (target is not AutomationElement automationElement)
			return base.RunKnownOperation(target, operation);

		switch (operation?.Trim())
		{
			case "Focus":
				return Focus(target);
			case "Select":
				if (TrySelectAutomation(automationElement))
					return ActionResult.Ok();
				break;
			case "Expand":
				if (TryExpandCollapseAutomation(automationElement, ExpandCollapseState.Expanded))
					return ActionResult.Ok();
				break;
			case "Collapse":
				if (TryExpandCollapseAutomation(automationElement, ExpandCollapseState.Collapsed))
					return ActionResult.Ok();
				break;
			case "Check":
				if (TryToggleAutomation(automationElement, true))
					return ActionResult.Ok();
				break;
			case "Uncheck":
				if (TryToggleAutomation(automationElement, false))
					return ActionResult.Ok();
				break;
			case "AcceptDialog":
			case "CancelDialog":
				if (TryInvokeAutomation(automationElement))
					return ActionResult.Ok();
				break;
		}

		return base.RunKnownOperation(target, operation);
	}

	internal static bool TryInvokeAutomation(AutomationElement automationElement, int clickCount = 1)
	{
		try
		{
			if (automationElement.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) && pattern is InvokePattern invokePattern)
			{
				for (var i = 0; i < Math.Max(1, clickCount); i++)
					invokePattern.Invoke();
				return true;
			}
		}
		catch (ElementNotAvailableException)
		{
		}

		return false;
	}

	private static bool TrySetAutomationValue(AutomationElement automationElement, string value)
	{
		try
		{
			if (automationElement.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
			{
				valuePattern.SetValue(value);
				return true;
			}
		}
		catch (ElementNotAvailableException)
		{
		}

		return false;
	}

	private static bool TrySelectAutomation(AutomationElement automationElement)
	{
		try
		{
			if (automationElement.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && pattern is SelectionItemPattern selectionItemPattern)
			{
				selectionItemPattern.Select();
				return true;
			}
		}
		catch (ElementNotAvailableException)
		{
		}

		return false;
	}

	private static bool TryExpandCollapseAutomation(AutomationElement automationElement, ExpandCollapseState state)
	{
		try
		{
			if (automationElement.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern) && pattern is ExpandCollapsePattern expandCollapsePattern)
			{
				if (state == ExpandCollapseState.Expanded)
					expandCollapsePattern.Expand();
				else
					expandCollapsePattern.Collapse();
				return true;
			}
		}
		catch (ElementNotAvailableException)
		{
		}

		return false;
	}

	private static bool TryToggleAutomation(AutomationElement automationElement, bool shouldBeChecked)
	{
		try
		{
			if (automationElement.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern) && pattern is TogglePattern togglePattern)
			{
				var desiredState = shouldBeChecked ? ToggleState.On : ToggleState.Off;
				for (var i = 0; i < 3 && togglePattern.Current.ToggleState != desiredState; i++)
					togglePattern.Toggle();
				return togglePattern.Current.ToggleState == desiredState;
			}
		}
		catch (ElementNotAvailableException)
		{
		}

		return false;
	}

	private static bool IsNativeTextProperty(string propertyName) =>
		string.Equals(propertyName, KnownProperties.FileName, StringComparison.Ordinal)
		|| string.Equals(propertyName, KnownProperties.Text, StringComparison.Ordinal)
		|| string.Equals(propertyName, KnownProperties.Value, StringComparison.Ordinal);
}
