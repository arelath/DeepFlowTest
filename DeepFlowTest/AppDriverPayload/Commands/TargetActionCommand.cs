namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using Serialize.Linq;
using Serialize.Linq.Factories;
using Serialize.Linq.Serializers;
using Forms = System.Windows.Forms;
using SerializeJsonSerializer = Serialize.Linq.Serializers.JsonSerializer;

internal static partial class TargetActionCommand
{
	private static readonly FactorySettings ExpressionFactorySettings = new() { AllowPrivateFieldAccess = true };

	public static object Click(ClickCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.Click, request.TargetId, treeService, target =>
		{
			var button = request.MouseButton?.Trim().ToLowerInvariant() ?? "left";
			if (button is not ("left" or "right"))
				return ActionResult.Unsupported($"Mouse button '{request.MouseButton}' is not supported.");

			if (target is UIElement uiElement)
				return ClickWpfElement(uiElement, button, request.ClickCount);

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
		WithTarget(ProtocolConstants.Commands.Focus, request.TargetId, treeService, target => FocusTarget(target)
			? ActionResult.Ok()
			: ActionResult.Unsupported($"Target type '{target.GetType().FullName}' cannot receive focus."));

	public static object TypeText(TypeTextCommandRequest request, TreeService treeService)
	{
		if (!string.IsNullOrWhiteSpace(request.TargetId))
		{
			return WithTarget(ProtocolConstants.Commands.TypeText, request.TargetId!, treeService, target => TypeTextIntoTarget(target, request.Text, request.ClearFirst));
		}

		var focusedElement = Keyboard.FocusedElement;
		if (focusedElement is null)
			return UnsupportedTarget("No focused WPF element is available for typing.");

		return ToResponse(TypeTextIntoTarget(focusedElement, request.Text, request.ClearFirst));
	}

	public static object KeyPress(KeyPressCommandRequest request, TreeService treeService)
	{
		if (!string.IsNullOrWhiteSpace(request.TargetId))
			return WithTarget(ProtocolConstants.Commands.KeyPress, request.TargetId!, treeService, target => SendKeysToTarget(target, request.Keys, request.DelayMs, request.EnsureForeground));

		var focusedElement = Keyboard.FocusedElement;
		if (focusedElement is null)
			return UnsupportedTarget("No focused WPF element is available for key input.");

		return ToResponse(SendKeysToTarget(focusedElement, request.Keys, request.DelayMs, request.EnsureForeground));
	}

	public static object SetProperty(SetPropertyCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.SetProperty, request.TargetId, treeService, target => SetProperty(target, request.PropertyName, request.PropertyValue));

	public static object RaiseEvent(RaiseEventCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.RaiseEvent, request.TargetId, treeService, target =>
		{
			if (TryGetExpressionPayload(request.GetRoutedEventArgs, out _))
				return RaiseExpressionRoutedEvent(target, request.GetRoutedEventArgs, request.TimeoutMs);

			var eventName = !string.IsNullOrWhiteSpace(request.EventName)
				? request.EventName
				: Convert.ToString(request.GetRoutedEventArgs, CultureInfo.InvariantCulture) ?? string.Empty;
			return RaiseKnownRoutedEvent(target, eventName);
		});

	public static object KnownRoutedEvent(KnownRoutedEventCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.KnownRoutedEvent, request.TargetId, treeService, target => RaiseKnownRoutedEvent(target, request.EventName));

	public static object KnownOperation(KnownOperationCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.KnownOperation, request.TargetId, treeService, target => RunKnownOperation(target, request.Operation));

	public static object Invoke(InvokeCommandRequest request, TreeService treeService)
	{
		if (!request.AllowUnsafeCode)
			return StandardIpcResponse.FromError("Invoke requires explicit unsafe-code opt-in.", ProtocolConstants.ErrorCodes.UnsupportedCommand, PayloadLog.CurrentCorrelationId);

		return WithTarget(ProtocolConstants.Commands.Invoke, request.TargetId, treeService, target =>
		{
			if (TryGetExpressionPayload(request.Code, out _))
			{
				var result = EvaluateExpressionPayload(target, request.Code, request.TimeoutMs, awaitTasks: true);
				return ActionResult.Ok(result);
			}

			var methodName = Convert.ToString(UnwrapJsonValue(request.Code), CultureInfo.InvariantCulture);
			if (string.IsNullOrWhiteSpace(methodName))
				return ActionResult.Unsupported("Invoke requires a public parameterless method name.");

			var method = target.GetType().GetMethod(methodName!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy, null, Type.EmptyTypes, null);
			if (method is null)
				return ActionResult.Unsupported($"Method '{methodName}' was not found or is not parameterless.");

			try
			{
				var result = method.Invoke(target, null);
				return ActionResult.Ok(AwaitTaskResult(result, request.TimeoutMs));
			}
			catch (TargetInvocationException ex) when (ex.InnerException is not null)
			{
				return ActionResult.Unsupported($"Invoke method '{methodName}' failed: {ex.InnerException.Message}");
			}
		});
	}

	private static object WithTarget(string commandName, string targetId, TreeService treeService, Func<object, ActionResult> action)
	{
		if (string.IsNullOrWhiteSpace(targetId))
			return UnsupportedTarget($"{commandName}: a target ID is required.");

		var resolution = treeService.ResolveTarget(targetId);
		if (resolution.Status != TargetIdResolutionStatus.Found)
		{
			var errorCode = resolution.Status == TargetIdResolutionStatus.Stale
				? ProtocolConstants.ErrorCodes.StaleTarget
				: ProtocolConstants.ErrorCodes.UnsupportedTarget;
			return StandardIpcResponse.FromError($"{commandName}: target '{targetId}' resolved as {resolution.Status}.", errorCode, PayloadLog.CurrentCorrelationId);
		}

		try
		{
			return ToResponse(action(resolution.Target!), commandName, targetId);
		}
		catch (TimeoutException ex)
		{
			return StandardIpcResponse.FromError(
				$"{commandName}: action timed out for target '{targetId}': {ex.Message}",
				ProtocolConstants.ErrorCodes.CommandTimeout,
				PayloadLog.CurrentCorrelationId);
		}
		catch (SerializationException ex)
		{
			return StandardIpcResponse.FromError(
				$"{commandName}: action result is not serializable for target '{targetId}': {ex.Message}",
				ProtocolConstants.ErrorCodes.ProtocolError,
				PayloadLog.CurrentCorrelationId);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			return StandardIpcResponse.FromError(
				$"{commandName}: action failed for target '{targetId}': {ex.Message}",
				ProtocolConstants.ErrorCodes.UnsupportedTarget,
				PayloadLog.CurrentCorrelationId);
		}
	}

	private static StandardIpcResponse ToResponse(ActionResult result, string? commandName = null, string? targetId = null) =>
		result.Success
			? SerializableSuccess(result.Value)
			: UnsupportedTarget(FormatActionError(result.Error ?? "The requested action is not supported for this target.", commandName, targetId));

	private static StandardIpcResponse SerializableSuccess(object? value)
	{
		var response = new StandardIpcResponse
		{
			Success = true,
			Status = ProtocolConstants.Statuses.Ok,
			Value = value,
		};

		if (value is null || CanPackResponse(response))
			return response;

		return StandardIpcResponse.UnserializableResult();
	}

	private static bool CanPackResponse(StandardIpcResponse response)
	{
		try
		{
			MessagePacker.Pack(response);
			return true;
		}
		catch (Exception ex) when (ex is ProtocolException or Newtonsoft.Json.JsonException or InvalidOperationException or NotSupportedException)
		{
			return false;
		}
	}

	private static StandardIpcResponse UnsupportedTarget(string error) =>
		StandardIpcResponse.FromError(error, ProtocolConstants.ErrorCodes.UnsupportedTarget, PayloadLog.CurrentCorrelationId);

	private static string FormatActionError(string error, string? commandName, string? targetId)
	{
		if (string.IsNullOrWhiteSpace(commandName) && string.IsNullOrWhiteSpace(targetId))
			return error;

		return $"{commandName ?? "action"}: target '{targetId ?? string.Empty}': {error}";
	}

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
			else if (string.Equals(keyText, "Delete", StringComparison.OrdinalIgnoreCase) || string.Equals(keyText, "Del", StringComparison.OrdinalIgnoreCase))
			{
				if (formsTextBox.SelectionLength > 0)
					formsTextBox.SelectedText = string.Empty;
			}
			else if (string.Equals(keyText, "Space", StringComparison.OrdinalIgnoreCase))
			{
				formsTextBox.SelectedText = " ";
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

		if (string.Equals(keyText, "Delete", StringComparison.OrdinalIgnoreCase) || string.Equals(keyText, "Del", StringComparison.OrdinalIgnoreCase))
		{
			if (textBox.SelectionLength > 0)
				textBox.SelectedText = string.Empty;
			return ActionResult.Ok();
		}

		if (!IsNonTextKey(keyText))
		{
			textBox.SelectedText = string.Equals(keyText, "Space", StringComparison.OrdinalIgnoreCase) ? " " : keyText;
			textBox.CaretIndex = textBox.Text.Length;
		}

		return ActionResult.Ok();
	}

	private static bool IsNonTextKey(string keyText) =>
		string.Equals(keyText, "Enter", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Tab", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Escape", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Esc", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Home", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "End", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "PageUp", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "PageDown", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Up", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Down", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Left", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Right", StringComparison.OrdinalIgnoreCase)
		|| (keyText.Length > 1 && keyText.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(keyText.Substring(1), out _));

	private static bool IsSelectAllShortcut(string keyText) =>
		string.Equals(keyText, "Control+A", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Ctrl+A", StringComparison.OrdinalIgnoreCase);

	private static ActionResult ClickWpfElement(UIElement target, string button, int clickCount)
	{
		var mouseButton = button == "right" ? MouseButton.Right : MouseButton.Left;
		var count = Math.Max(1, clickCount);
		for (var i = 0; i < count; i++)
		{
			RaiseMouseButtonEvent(target, UIElement.PreviewMouseDownEvent, mouseButton);
			RaiseMouseButtonEvent(target, UIElement.MouseDownEvent, mouseButton);
			RaiseMouseButtonEvent(target, UIElement.PreviewMouseUpEvent, mouseButton);
			RaiseMouseButtonEvent(target, UIElement.MouseUpEvent, mouseButton);

			if (target is ButtonBase buttonBase && mouseButton == MouseButton.Left)
				buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
		}

		if (count > 1 && mouseButton == MouseButton.Left && target is Control control)
			RaiseMouseButtonEvent(control, Control.MouseDoubleClickEvent, MouseButton.Left);

		if (mouseButton == MouseButton.Right)
			OpenContextMenu(target);

		return ActionResult.Ok();
	}

	private static void RaiseMouseButtonEvent(UIElement target, RoutedEvent routedEvent, MouseButton button)
	{
		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
		{
			RoutedEvent = routedEvent,
			Source = target,
		};
		target.RaiseEvent(args);
	}

	private static void OpenContextMenu(UIElement target)
	{
		if (target is not DependencyObject dependencyObject)
			return;

		var contextMenu = ContextMenuService.GetContextMenu(dependencyObject);
		if (contextMenu is null)
			return;

		contextMenu.PlacementTarget = target;
		contextMenu.Placement = PlacementMode.Bottom;
		contextMenu.IsOpen = true;
	}

	private static ActionResult SetProperty(object target, string propertyName, object? rawValue)
	{
		if (string.IsNullOrWhiteSpace(propertyName))
			return ActionResult.Unsupported("Property name is required.");

		var value = TryGetExpressionPayload(rawValue, out _)
			? EvaluateExpressionPayload(target, rawValue, timeoutMs: null, awaitTasks: true)
			: UnwrapJsonValue(rawValue);

		if (IsNativeTextProperty(propertyName))
		{
			var textValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
			if (target is AutomationElement automationElement && TrySetAutomationValue(automationElement, textValue))
				return ActionResult.Ok();
			if (target is IntPtr hwnd && TrySetNativeWindowText(hwnd, textValue, clearFirst: true))
				return ActionResult.Ok();
		}

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

	private static bool IsNativeTextProperty(string propertyName) =>
		string.Equals(propertyName, "FileName", StringComparison.Ordinal)
		|| string.Equals(propertyName, "Text", StringComparison.Ordinal)
		|| string.Equals(propertyName, "Value", StringComparison.Ordinal);

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

	private static ActionResult RaiseExpressionRoutedEvent(object target, object? expressionPayload, int? timeoutMs)
	{
		if (target is not UIElement uiElement && target is not ContentElement)
			return ActionResult.Unsupported($"Target type '{target.GetType().FullName}' does not support routed events.");

		var evaluated = EvaluateExpressionPayload(target, expressionPayload, timeoutMs, awaitTasks: false);
		if (evaluated is not RoutedEventArgs args)
			return ActionResult.Unsupported("Routed event expression did not return RoutedEventArgs.");
		if (args.RoutedEvent is null)
			return ActionResult.Unsupported("Routed event expression returned args without RoutedEvent.");

		args.Source ??= target;
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
			"MouseDoubleClick" => Control.MouseDoubleClickEvent,
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
				if (target is Forms.Control formsBringIntoViewControl)
				{
					if (formsBringIntoViewControl.Parent is Forms.ScrollableControl scrollableControl)
						scrollableControl.ScrollControlIntoView(formsBringIntoViewControl);
					return ActionResult.Ok();
				}

				if (target is FrameworkElement frameworkElement)
				{
					frameworkElement.BringIntoView();
					return ActionResult.Ok();
				}

				if (TryInvokeNoArg(target, "BringIntoView"))
					return ActionResult.Ok();

				break;
			case "Select":
				if (target is Forms.Control formsSelectControl)
				{
					formsSelectControl.Select();
					return ActionResult.Ok();
				}

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

				if (TrySetBooleanProperty(target, ["IsSelected"], true) || TryInvokeNoArg(target, "Select"))
					return ActionResult.Ok();

				if (target is AutomationElement automationElement && TrySelectAutomation(automationElement))
					return ActionResult.Ok();

				break;
			case "Expand":
				if (target is Forms.ComboBox formsExpandComboBox)
				{
					formsExpandComboBox.DroppedDown = true;
					return ActionResult.Ok();
				}

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

				if (TrySetBooleanProperty(target, ["IsExpanded"], true) || TryInvokeNoArg(target, "Expand"))
					return ActionResult.Ok();

				if (target is AutomationElement expandAutomationElement && TryExpandCollapseAutomation(expandAutomationElement, ExpandCollapseState.Expanded))
					return ActionResult.Ok();

				break;
			case "Collapse":
				if (target is Forms.ComboBox formsCollapseComboBox)
				{
					formsCollapseComboBox.DroppedDown = false;
					return ActionResult.Ok();
				}

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

				if (TrySetBooleanProperty(target, ["IsExpanded"], false) || TryInvokeNoArg(target, "Collapse"))
					return ActionResult.Ok();

				if (target is AutomationElement collapseAutomationElement && TryExpandCollapseAutomation(collapseAutomationElement, ExpandCollapseState.Collapsed))
					return ActionResult.Ok();

				break;
			case "Check":
				if (TrySetBooleanProperty(target, ["IsChecked", "Checked"], true) || TryInvokeNoArg(target, "Check"))
					return ActionResult.Ok();

				if (target is AutomationElement checkAutomationElement && TryToggleAutomation(checkAutomationElement, true))
					return ActionResult.Ok();

				break;
			case "Uncheck":
				if (TrySetBooleanProperty(target, ["IsChecked", "Checked"], false) || TryInvokeNoArg(target, "Uncheck"))
					return ActionResult.Ok();

				if (target is AutomationElement uncheckAutomationElement && TryToggleAutomation(uncheckAutomationElement, false))
					return ActionResult.Ok();

				break;
			case "AcceptDialog":
			case "CancelDialog":
				var accept = operation == "AcceptDialog";
				if (target is Window window)
				{
					window.DialogResult = accept;
					window.Close();
					return ActionResult.Ok();
				}

				if (target is Forms.Form form)
				{
					form.DialogResult = accept ? Forms.DialogResult.OK : Forms.DialogResult.Cancel;
					form.Close();
					return ActionResult.Ok();
				}

				if (target is AutomationElement dialogAutomationElement && TryInvokeAutomation(dialogAutomationElement))
					return ActionResult.Ok();

				if (target is IntPtr dialogHwnd && TryInvokeNativeDialogButton(dialogHwnd, accept))
					return ActionResult.Ok();

				break;
		}

		return ActionResult.Unsupported($"Known operation '{operation}' is not supported for target type '{target.GetType().FullName}'.");
	}

	private static bool TrySetBooleanProperty(object target, IReadOnlyList<string> propertyNames, bool value)
	{
		foreach (var propertyName in propertyNames)
		{
			var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
			if (property is null || !property.CanWrite || property.GetIndexParameters().Length != 0)
				continue;

			var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
			if (propertyType != typeof(bool))
				continue;

			try
			{
				property.SetValue(target, value, null);
				return true;
			}
			catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidOperationException)
			{
			}
		}

		return false;
	}

	private static bool TryInvokeNoArg(object target, string methodName)
	{
		var method = target.GetType()
			.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
			.FirstOrDefault(method => string.Equals(method.Name, methodName, StringComparison.Ordinal) && method.GetParameters().Length == 0);
		if (method is null)
			return false;

		try
		{
			method.Invoke(target, null);
			return true;
		}
		catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidOperationException)
		{
			return false;
		}
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
				if (button is not null && TryInvokeAutomation(button))
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
			if (NativeMethods.GetDlgCtrlID(child) == controlId && TryClickNativeWindow(child, "left", 1))
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

	private static object? EvaluateExpressionPayload(object target, object? rawPayload, int? timeoutMs, bool awaitTasks)
	{
		if (!TryGetExpressionPayload(rawPayload, out var payload))
			return UnwrapJsonValue(rawPayload);

		var expression = DeserializeExpression(payload);
		var result = expression.Compile().DynamicInvoke(target);
		return awaitTasks ? AwaitTaskResult(result, timeoutMs) : result;
	}

	private static bool TryGetExpressionPayload(object? rawPayload, out ExpressionMatcherPayload payload)
	{
		payload = null!;
		if (rawPayload is null)
			return false;

		try
		{
			payload = MessagePacker.ConvertTo<ExpressionMatcherPayload>(rawPayload);
			return !string.IsNullOrWhiteSpace(payload.ExpressionJson);
		}
		catch (Exception ex) when (ex is InvalidCastException or ArgumentException or Newtonsoft.Json.JsonException)
		{
			return false;
		}
	}

	private static LambdaExpression DeserializeExpression(ExpressionMatcherPayload payload)
	{
		if (string.IsNullOrWhiteSpace(payload.ExpressionJson))
			throw new InvalidOperationException("Expression payload is empty.");

		var serializer = new ExpressionSerializer(new SerializeJsonSerializer(), ExpressionFactorySettings);
		var expression = serializer.DeserializeText(payload.ExpressionJson, new ExpressionContext { AllowPrivateFieldAccess = true });
		return expression as LambdaExpression
			?? throw new InvalidOperationException("Expression payload did not deserialize to a lambda expression.");
	}

	private static object? AwaitTaskResult(object? result, int? timeoutMs)
	{
		if (result is not Task task)
			return result;

		if (timeoutMs is > 0)
		{
			if (!task.Wait(timeoutMs.Value))
				throw new TimeoutException("Expression task did not complete within the command timeout.");
		}
		else
		{
			task.GetAwaiter().GetResult();
		}

		var resultProperty = task.GetType().GetProperty(nameof(Task<object>.Result), BindingFlags.Instance | BindingFlags.Public);
		return resultProperty?.GetValue(task, null);
	}

}
