namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using Newtonsoft.Json.Linq;
using Forms = System.Windows.Forms;

internal static class TargetActionCommand
{
	public static object Click(ClickCommandRequest request, TreeService treeService) =>
		WithTarget(request.TargetId, treeService, target =>
		{
			var button = request.MouseButton?.Trim().ToLowerInvariant() ?? "left";
			if (target is ButtonBase buttonBase && button == "left")
			{
				for (var i = 0; i < Math.Max(1, request.ClickCount); i++)
					buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
				return ActionResult.Ok();
			}

			if (target is UIElement uiElement && button == "right")
			{
				RaiseMouseButtonEvent(uiElement, UIElement.MouseRightButtonUpEvent, MouseButton.Right);
				return ActionResult.Ok();
			}

			if (target is Forms.Button formsButton && button == "left")
			{
				for (var i = 0; i < Math.Max(1, request.ClickCount); i++)
					formsButton.PerformClick();
				return ActionResult.Ok();
			}

			if (target is Forms.Control formsControl && TryClickNativeWindow(formsControl.Handle, button, request.ClickCount))
				return ActionResult.Ok();

			if (target is AutomationElement automationElement && button == "left" && TryInvokeAutomation(automationElement, request.ClickCount))
				return ActionResult.Ok();

			if (target is IntPtr hwnd && TryClickNativeWindow(hwnd, button, request.ClickCount))
				return ActionResult.Ok();

			return ActionResult.Unsupported($"Target type '{target.GetType().FullName}' does not support {button} click.");
		});

	public static object Focus(FocusCommandRequest request, TreeService treeService) =>
		WithTarget(request.TargetId, treeService, target => FocusTarget(target)
			? ActionResult.Ok()
			: ActionResult.Unsupported($"Target type '{target.GetType().FullName}' cannot receive focus."));

	public static object TypeText(TypeTextCommandRequest request, TreeService treeService)
	{
		if (!string.IsNullOrWhiteSpace(request.TargetId))
		{
			return WithTarget(request.TargetId!, treeService, target => TypeTextIntoTarget(target, request.Text, request.ClearFirst));
		}

		var focusedElement = Keyboard.FocusedElement;
		if (focusedElement is null)
			return UnsupportedTarget("No focused WPF element is available for typing.");

		return ToResponse(TypeTextIntoTarget(focusedElement, request.Text, request.ClearFirst));
	}

	public static object KeyPress(KeyPressCommandRequest request, TreeService treeService)
	{
		if (!string.IsNullOrWhiteSpace(request.TargetId))
			return WithTarget(request.TargetId!, treeService, target => SendKeysToTarget(target, request.Keys, request.DelayMs, request.EnsureForeground));

		var focusedElement = Keyboard.FocusedElement;
		if (focusedElement is null)
			return UnsupportedTarget("No focused WPF element is available for key input.");

		return ToResponse(SendKeysToTarget(focusedElement, request.Keys, request.DelayMs, request.EnsureForeground));
	}

	public static object SetProperty(SetPropertyCommandRequest request, TreeService treeService) =>
		WithTarget(request.TargetId, treeService, target => SetProperty(target, request.PropertyName, request.PropertyValue));

	public static object RaiseEvent(RaiseEventCommandRequest request, TreeService treeService) =>
		WithTarget(request.TargetId, treeService, target =>
		{
			var eventName = !string.IsNullOrWhiteSpace(request.EventName)
				? request.EventName
				: Convert.ToString(request.GetRoutedEventArgs, CultureInfo.InvariantCulture) ?? string.Empty;
			return RaiseKnownRoutedEvent(target, eventName);
		});

	public static object KnownRoutedEvent(KnownRoutedEventCommandRequest request, TreeService treeService) =>
		WithTarget(request.TargetId, treeService, target => RaiseKnownRoutedEvent(target, request.EventName));

	public static object KnownOperation(KnownOperationCommandRequest request, TreeService treeService) =>
		WithTarget(request.TargetId, treeService, target => RunKnownOperation(target, request.Operation));

	public static object Invoke(InvokeCommandRequest request, TreeService treeService)
	{
		if (!request.AllowUnsafeCode)
			return StandardIpcResponse.FromError("Invoke requires explicit unsafe-code opt-in.", ProtocolConstants.ErrorCodes.UnsupportedCommand, LogCorrelationId());

		return WithTarget(request.TargetId, treeService, target =>
		{
			var methodName = Convert.ToString(UnwrapJsonValue(request.Code), CultureInfo.InvariantCulture);
			if (string.IsNullOrWhiteSpace(methodName))
				return ActionResult.Unsupported("Invoke requires a public parameterless method name.");

			var method = target.GetType().GetMethod(methodName!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy, null, Type.EmptyTypes, null);
			if (method is null)
				return ActionResult.Unsupported($"Method '{methodName}' was not found or is not parameterless.");

			method.Invoke(target, null);
			return ActionResult.Ok();
		});
	}

	private static object WithTarget(string targetId, TreeService treeService, Func<object, ActionResult> action)
	{
		if (string.IsNullOrWhiteSpace(targetId))
			return UnsupportedTarget("A target ID is required.");

		var resolution = treeService.ResolveTarget(targetId);
		if (resolution.Status != TargetIdResolutionStatus.Found)
		{
			var errorCode = resolution.Status == TargetIdResolutionStatus.Stale
				? ProtocolConstants.ErrorCodes.StaleTarget
				: ProtocolConstants.ErrorCodes.UnsupportedTarget;
			return StandardIpcResponse.FromError($"Target '{targetId}' resolved as {resolution.Status}.", errorCode, LogCorrelationId());
		}

		return ToResponse(action(resolution.Target!));
	}

	private static StandardIpcResponse ToResponse(ActionResult result) =>
		result.Success
			? StandardIpcResponse.Ok()
			: UnsupportedTarget(result.Error ?? "The requested action is not supported for this target.");

	private static StandardIpcResponse UnsupportedTarget(string error) =>
		StandardIpcResponse.FromError(error, ProtocolConstants.ErrorCodes.UnsupportedTarget, LogCorrelationId());

	private static ActionResult TypeTextIntoTarget(object target, string text, bool clearFirst)
	{
		if (target is TextBox textBox)
		{
			if (clearFirst)
				textBox.Clear();
			textBox.SelectedText = text ?? string.Empty;
			textBox.CaretIndex = textBox.Text.Length;
			return ActionResult.Ok();
		}

		if (target is PasswordBox passwordBox)
		{
			if (clearFirst)
				passwordBox.Clear();
			passwordBox.Password += text ?? string.Empty;
			return ActionResult.Ok();
		}

		if (target is ComboBox comboBox)
		{
			comboBox.Text = clearFirst ? text ?? string.Empty : comboBox.Text + (text ?? string.Empty);
			return ActionResult.Ok();
		}

		if (target is Forms.TextBoxBase textBoxBase)
		{
			if (clearFirst)
				textBoxBase.Clear();
			textBoxBase.SelectedText = text ?? string.Empty;
			return ActionResult.Ok();
		}

		if (target is Forms.ComboBox formsComboBox)
		{
			formsComboBox.Text = clearFirst ? text ?? string.Empty : formsComboBox.Text + (text ?? string.Empty);
			return ActionResult.Ok();
		}

		if (target is AutomationElement automationElement && TrySetAutomationValue(automationElement, clearFirst ? text ?? string.Empty : text ?? string.Empty))
			return ActionResult.Ok();

		if (target is IntPtr hwnd && TrySetNativeWindowText(hwnd, text ?? string.Empty, clearFirst))
			return ActionResult.Ok();

		return ActionResult.Unsupported($"Target type '{target.GetType().FullName}' does not support text input.");
	}

	private static ActionResult SendKeysToTarget(object target, object? keys, int delayMs, bool ensureForeground)
	{
		var keyText = Convert.ToString(UnwrapJsonValue(keys), CultureInfo.InvariantCulture) ?? string.Empty;
		if (string.IsNullOrEmpty(keyText))
			return ActionResult.Unsupported("Key input cannot be empty.");

		if (ensureForeground)
			FocusTarget(target);

		if (delayMs > 0)
			Thread.Sleep(delayMs);

		if (target is TextBox textBox)
			return SendKeysToTextBox(textBox, keyText);

		if (target is Forms.TextBoxBase formsTextBox)
		{
			if (IsSelectAllShortcut(keyText))
			{
				formsTextBox.SelectAll();
			}
			else if (string.Equals(keyText, "Backspace", StringComparison.OrdinalIgnoreCase))
			{
				if (formsTextBox.Text.Length != 0)
					formsTextBox.Text = formsTextBox.Text.Substring(0, formsTextBox.Text.Length - 1);
			}
			else if (!IsNonTextKey(keyText))
			{
				formsTextBox.Text += keyText;
			}

			return ActionResult.Ok();
		}

		return ActionResult.Unsupported($"Target type '{target.GetType().FullName}' does not support key input.");
	}

	private static ActionResult SendKeysToTextBox(TextBox textBox, string keyText)
	{
		if (IsSelectAllShortcut(keyText))
		{
			textBox.SelectAll();
			return ActionResult.Ok();
		}

		if (string.Equals(keyText, "Backspace", StringComparison.OrdinalIgnoreCase))
		{
			if (textBox.Text.Length != 0)
				textBox.Text = textBox.Text.Substring(0, textBox.Text.Length - 1);
			textBox.CaretIndex = textBox.Text.Length;
			return ActionResult.Ok();
		}

		if (!IsNonTextKey(keyText))
		{
			textBox.SelectedText = keyText;
			textBox.CaretIndex = textBox.Text.Length;
		}

		return ActionResult.Ok();
	}

	private static bool IsNonTextKey(string keyText) =>
		string.Equals(keyText, "Enter", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Tab", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Escape", StringComparison.OrdinalIgnoreCase);

	private static bool IsSelectAllShortcut(string keyText) =>
		string.Equals(keyText, "Control+A", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Ctrl+A", StringComparison.OrdinalIgnoreCase);

	private static void RaiseMouseButtonEvent(UIElement target, RoutedEvent routedEvent, MouseButton button)
	{
		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
		{
			RoutedEvent = routedEvent,
			Source = target,
		};
		target.RaiseEvent(args);
	}

	private static ActionResult SetProperty(object target, string propertyName, object? rawValue)
	{
		if (string.IsNullOrWhiteSpace(propertyName))
			return ActionResult.Unsupported("Property name is required.");

		var value = UnwrapJsonValue(rawValue);
		var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
		if (property is not null)
		{
			if (!property.CanWrite || property.GetIndexParameters().Length != 0)
				return ActionResult.Unsupported($"Property '{propertyName}' is read-only.");

			property.SetValue(target, ConvertValue(value, property.PropertyType), null);
			return ActionResult.Ok();
		}

		if (target is DependencyObject dependencyObject && TryFindDependencyProperty(target.GetType(), propertyName, out var dependencyProperty))
		{
			dependencyObject.SetValue(dependencyProperty, ConvertValue(value, dependencyProperty.PropertyType));
			return ActionResult.Ok();
		}

		return ActionResult.Unsupported($"Property '{propertyName}' was not found.");
	}

	private static ActionResult RaiseKnownRoutedEvent(object target, string eventName)
	{
		if (target is not UIElement uiElement && target is not ContentElement)
			return ActionResult.Unsupported($"Target type '{target.GetType().FullName}' does not support routed events.");

		var routedEvent = ResolveRoutedEvent(eventName);
		if (routedEvent is null)
			return ActionResult.Unsupported($"Routed event '{eventName}' is not allow-listed.");

		var args = new RoutedEventArgs(routedEvent, target);
		if (target is UIElement targetElement)
			targetElement.RaiseEvent(args);
		else
			((ContentElement)target).RaiseEvent(args);

		return ActionResult.Ok();
	}

	private static RoutedEvent? ResolveRoutedEvent(string eventName)
	{
		return eventName?.Trim() switch
		{
			"Click" => ButtonBase.ClickEvent,
			"Checked" => ToggleButton.CheckedEvent,
			"Unchecked" => ToggleButton.UncheckedEvent,
			"Expanded" => Expander.ExpandedEvent,
			"Collapsed" => Expander.CollapsedEvent,
			_ => null,
		};
	}

	private static ActionResult RunKnownOperation(object target, string operation)
	{
		switch (operation?.Trim())
		{
			case "Focus":
				return FocusTarget(target) ? ActionResult.Ok() : ActionResult.Unsupported("Target cannot receive focus.");
			case "BringIntoView":
				if (target is FrameworkElement frameworkElement)
				{
					frameworkElement.BringIntoView();
					return ActionResult.Ok();
				}

				break;
			case "Select":
				if (target is ListBoxItem listBoxItem)
				{
					listBoxItem.IsSelected = true;
					return ActionResult.Ok();
				}

				if (target is ComboBoxItem comboBoxItem)
				{
					comboBoxItem.IsSelected = true;
					return ActionResult.Ok();
				}

				if (target is AutomationElement automationElement && TrySelectAutomation(automationElement))
					return ActionResult.Ok();

				break;
			case "Expand":
				if (target is Expander expander)
				{
					expander.IsExpanded = true;
					return ActionResult.Ok();
				}

				if (target is ComboBox comboBox)
				{
					comboBox.IsDropDownOpen = true;
					return ActionResult.Ok();
				}

				if (target is AutomationElement expandAutomationElement && TryExpandCollapseAutomation(expandAutomationElement, ExpandCollapseState.Expanded))
					return ActionResult.Ok();

				break;
			case "Collapse":
				if (target is Expander collapseExpander)
				{
					collapseExpander.IsExpanded = false;
					return ActionResult.Ok();
				}

				if (target is ComboBox collapseComboBox)
				{
					collapseComboBox.IsDropDownOpen = false;
					return ActionResult.Ok();
				}

				if (target is AutomationElement collapseAutomationElement && TryExpandCollapseAutomation(collapseAutomationElement, ExpandCollapseState.Collapsed))
					return ActionResult.Ok();

				break;
			case "Check":
				if (target is ToggleButton toggleButton)
				{
					toggleButton.IsChecked = true;
					return ActionResult.Ok();
				}

				if (target is AutomationElement checkAutomationElement && TryToggleAutomation(checkAutomationElement, true))
					return ActionResult.Ok();

				break;
			case "Uncheck":
				if (target is ToggleButton uncheckToggleButton)
				{
					uncheckToggleButton.IsChecked = false;
					return ActionResult.Ok();
				}

				if (target is AutomationElement uncheckAutomationElement && TryToggleAutomation(uncheckAutomationElement, false))
					return ActionResult.Ok();

				break;
			case "AcceptDialog":
			case "CancelDialog":
				if (target is Window window)
				{
					window.DialogResult = operation == "AcceptDialog";
					window.Close();
					return ActionResult.Ok();
				}

				break;
		}

		return ActionResult.Unsupported($"Known operation '{operation}' is not supported for target type '{target.GetType().FullName}'.");
	}

	private static bool FocusTarget(object target)
	{
		switch (target)
		{
			case IInputElement inputElement:
				return inputElement.Focus();
			case Forms.Control control:
				return control.Focus();
			case AutomationElement automationElement:
				automationElement.SetFocus();
				return true;
			case IntPtr hwnd when hwnd != IntPtr.Zero:
				return NativeMethods.SetForegroundWindow(hwnd);
			default:
				return false;
		}
	}

	private static bool TryInvokeAutomation(AutomationElement automationElement, int clickCount = 1)
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

	private static bool TryClickNativeWindow(IntPtr hwnd, string mouseButton, int clickCount)
	{
		if (hwnd == IntPtr.Zero || !NativeMethods.GetClientRect(hwnd, out var rect))
			return false;

		var x = Math.Max(0, (rect.Right - rect.Left) / 2);
		var y = Math.Max(0, (rect.Bottom - rect.Top) / 2);
		var lParam = new IntPtr((y << 16) | (x & 0xffff));
		var downMessage = string.Equals(mouseButton, "right", StringComparison.OrdinalIgnoreCase)
			? NativeMethods.WM_RBUTTONDOWN
			: NativeMethods.WM_LBUTTONDOWN;
		var upMessage = string.Equals(mouseButton, "right", StringComparison.OrdinalIgnoreCase)
			? NativeMethods.WM_RBUTTONUP
			: NativeMethods.WM_LBUTTONUP;
		var count = Math.Max(1, clickCount);
		for (var i = 0; i < count; i++)
		{
			if (string.Equals(mouseButton, "left", StringComparison.OrdinalIgnoreCase))
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

	private static bool TryFindDependencyProperty(Type targetType, string propertyName, out DependencyProperty dependencyProperty)
	{
		var fieldName = propertyName.EndsWith("Property", StringComparison.Ordinal) ? propertyName : propertyName + "Property";
		for (var type = targetType; type is not null; type = type.BaseType)
		{
			var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
			if (field?.GetValue(null) is DependencyProperty found)
			{
				dependencyProperty = found;
				return true;
			}
		}

		dependencyProperty = null!;
		return false;
	}

	private static object? ConvertValue(object? value, Type targetType)
	{
		if (value is null)
			return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
				? Activator.CreateInstance(targetType)
				: null;

		var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
		if (underlyingType.IsInstanceOfType(value))
			return value;

		if (underlyingType.IsEnum)
			return Enum.Parse(underlyingType, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);

		return Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
	}

	private static object? UnwrapJsonValue(object? value)
	{
		return value switch
		{
			JValue jValue => jValue.Value,
			JToken token => token.ToObject<object>(),
			_ => value,
		};
	}

	private static string LogCorrelationId()
	{
		return System.IO.Path.GetFileNameWithoutExtension(PayloadLog.CurrentLogPath);
	}

	private readonly struct ActionResult
	{
		private ActionResult(bool success, string? error)
		{
			Success = success;
			Error = error;
		}

		public bool Success { get; }

		public string? Error { get; }

		public static ActionResult Ok() => new(true, null);

		public static ActionResult Unsupported(string error) => new(false, error);
	}

	private static class NativeMethods
	{
		public const int WM_LBUTTONDOWN = 0x0201;
		public const int WM_LBUTTONUP = 0x0202;
		public const int WM_RBUTTONDOWN = 0x0204;
		public const int WM_RBUTTONUP = 0x0205;
		public const int WM_SETTEXT = 0x000C;
		public const int BM_CLICK = 0x00F5;

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern bool SetForegroundWindow(IntPtr hwnd);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

		[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
		public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

		[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
		public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

		[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
		public static extern int GetWindowTextLength(IntPtr hwnd);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
	}

	private struct NativeRect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}
}
