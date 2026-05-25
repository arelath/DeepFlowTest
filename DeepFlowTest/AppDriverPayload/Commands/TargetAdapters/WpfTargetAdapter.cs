namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.AppDriverPayload.Diagnostics;
using DeepFlowTest.Contracts;
using DeepFlowTest.Shared;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.SelectionHighlight;

internal sealed class WpfTargetAdapter : UiTargetAdapterBase
{
	private const BindingFlags InvokeCommandBindings = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
	private const BindingFlags RoutedEventBindings = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

	public override bool CanHandle(object target) =>
		target is IInputElement or DependencyObject;

	public override ActionResult Click(object target, MouseButtonKind button, int clickCount) =>
		target is UIElement uiElement
			? PerformClick(uiElement, button, clickCount)
			: base.Click(target, button, clickCount);

	public override ActionResult Focus(object target) =>
		target is IInputElement inputElement && inputElement.Focus()
			? ActionResult.Ok()
			: ActionResult.Unsupported("Target cannot receive focus.");

	public override ActionResult TypeText(object target, string text, bool clearFirst)
	{
		if (target is not UIElement uiElement)
			return base.TypeText(target, text, clearFirst);

		uiElement.Focus();
		if (clearFirst)
			ClearText(target);
		KeyboardInput.TypeTextComposition(text);
		return ActionResult.Ok();
	}

	public override ActionResult SendKeys(object target, object? keys, string keyText, int delayMs)
	{
		if (TryHandleMenuEscape(target, keyText))
			return ActionResult.Ok();

		if (TryHandleFocusNavigation(target, keyText))
			return ActionResult.Ok();

		if (target is TextBox textBox)
		{
			if (ShouldHandleTextBoxKeyInternally(keyText))
				return SendKeysToTextBox(textBox, keyText);

			if (TryInvokeKeyGestureBindings(textBox, keyText))
				return ActionResult.Ok();

			return TargetKeyboardInput.SendKeysToForeground(keys, delayMs);
		}

		if (target is UIElement or ContentElement)
		{
			Focus(target);
			if (TryInvokeKeyGestureBindings(target, keyText))
				return ActionResult.Ok();

			return TargetKeyboardInput.SendKeysToForeground(keys, delayMs);
		}

		return base.SendKeys(target, keys, keyText, delayMs);
	}

	public override bool TryEnsureForeground(object target)
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

	public override PointerTargetResult GetPointerTarget(object target, PointerAnchor anchor)
	{
		if (target is not UIElement uiElement)
			return base.GetPointerTarget(target, anchor);

		if (!uiElement.IsVisible)
			return PointerTargetResult.Unsupported("WPF target is not visible.");
		if (!uiElement.IsEnabled)
			return PointerTargetResult.Unsupported("WPF target is not enabled.");

		var width = uiElement.RenderSize.Width;
		var height = uiElement.RenderSize.Height;
		if (!IsPositiveFinite(width) || !IsPositiveFinite(height))
			return PointerTargetResult.Unsupported("WPF target has no renderable size.");

		try
		{
			var local = new Point(width * anchor.X, height * anchor.Y);
			var screen = uiElement.PointToScreen(local);
			return PointerTargetResult.FromTarget(new PointerTarget(
				(int)Math.Round(screen.X),
				(int)Math.Round(screen.Y),
				GetOwnerHwnd(uiElement),
				uiElement.GetType().FullName ?? uiElement.GetType().Name));
		}
		catch (InvalidOperationException ex)
		{
			return PointerTargetResult.Unsupported($"WPF target screen coordinates could not be resolved: {ex.Message}");
		}
	}

	internal static ActionResult PerformInjectedDragAndDrop(
		object sourceTarget,
		object destinationTarget,
		PointerAnchor sourceAnchor,
		PointerAnchor destinationAnchor,
		int durationMs,
		int stepIntervalMs)
	{
		if (sourceTarget is not UIElement source || destinationTarget is not UIElement destination)
			return ActionResult.Unsupported("Injected drag events require WPF UIElement source and destination targets.");
		if (!source.IsVisible)
			return ActionResult.Unsupported("WPF source target is not visible.");
		if (!source.IsEnabled)
			return ActionResult.Unsupported("WPF source target is not enabled.");
		if (!destination.IsVisible)
			return ActionResult.Unsupported("WPF destination target is not visible.");
		if (!destination.IsEnabled)
			return ActionResult.Unsupported("WPF destination target is not enabled.");

		if (!TryGetScreenPoint(source, sourceAnchor, out var sourceScreen, out var error))
			return ActionResult.Unsupported($"WPF source target screen coordinates could not be resolved: {error}");
		if (!TryGetScreenPoint(destination, destinationAnchor, out var destinationScreen, out error))
			return ActionResult.Unsupported($"WPF destination target screen coordinates could not be resolved: {error}");

		TryEnsureAppHooks();
		using var syntheticMouseInput = AppHooks.BeginSyntheticMouseInput();
		try
		{
			AppHooks.SetSyntheticMouseScreenPosition(sourceScreen);
			VirtualPointerService.BeginDrag(sourceScreen, GetOwnerHwnd(source));
			AppHooks.SetButton(MouseButton.Left, isPressed: true);
			var sourceTargets = GetAscendingVisualTree(source);
			RaiseMouseButtonEvent(source, UIElement.PreviewMouseDownEvent, MouseButton.Left, sourceTargets);
			RaiseMouseButtonEvent(source, UIElement.MouseDownEvent, MouseButton.Left, sourceTargets);

			var steps = Math.Max(1, durationMs / Math.Max(1, stepIntervalMs));
			for (var i = 1; i <= steps; i++)
			{
				var progress = (double)i / steps;
				var currentScreen = Interpolate(sourceScreen, destinationScreen, progress);
				AppHooks.SetSyntheticMouseScreenPosition(currentScreen);
				VirtualPointerService.DragMove(currentScreen);
				RaiseMouseMoveEvent(source);
			}

			AppHooks.SetSyntheticMouseScreenPosition(destinationScreen);
			VirtualPointerService.EndDrag(destinationScreen);
			RaiseMouseMoveEvent(destination);
			AppHooks.SetButton(MouseButton.Left, isPressed: false);
			var destinationTargets = GetAscendingVisualTree(destination);
			RaiseMouseButtonEvent(destination, UIElement.PreviewMouseUpEvent, MouseButton.Left, destinationTargets);
			RaiseMouseButtonEvent(destination, UIElement.MouseUpEvent, MouseButton.Left, destinationTargets);
		}
		finally
		{
			AppHooks.ResetMouseState();
		}

		return ActionResult.Ok();
	}

	public override ActionResult SetProperty(object target, string propertyName, object? value)
	{
		if (TrySetClrProperty(target, propertyName, value, out var result))
			return result;

		if (target is DependencyObject dependencyObject && TryFindDependencyProperty(target.GetType(), propertyName, out var dependencyProperty))
		{
			dependencyObject.SetValue(dependencyProperty, TargetValueConverter.ConvertValue(value, dependencyProperty.PropertyType));
			return ActionResult.Ok();
		}

		return ActionResult.Unsupported($"Property '{propertyName}' was not found.");
	}

	public override ActionResult RaiseKnownRoutedEvent(object target, string eventName)
	{
		if (target is not UIElement && target is not ContentElement)
			return base.RaiseKnownRoutedEvent(target, eventName);

		var routedEvent = ResolveRoutedEvent(target.GetType(), eventName);
		if (routedEvent is null)
			return ActionResult.Unsupported($"Routed event '{eventName}' is not allow-listed.");

		var args = CreateKnownRoutedEventArgs(eventName, routedEvent, target);
		ReportVirtualPointerForKnownRoutedEvent(target, eventName);
		if (args is MouseButtonEventArgs && string.Equals(eventName?.Trim(), "MouseDoubleClick", StringComparison.Ordinal))
			InvokeMouseGestureBindings(target, MouseButton.Left, 2);

		if (target is UIElement targetElement)
			targetElement.RaiseEvent(args);
		else
			((ContentElement)target).RaiseEvent(args);

		return ActionResult.Ok();
	}

	public override ActionResult RaiseExpressionRoutedEvent(object target, object? expressionPayload, int? timeoutMs)
	{
		if (target is not UIElement && target is not ContentElement)
			return base.RaiseExpressionRoutedEvent(target, expressionPayload, timeoutMs);

		var evaluated = TargetExpressionEvaluator.Evaluate(target, expressionPayload, timeoutMs, awaitTasks: false);
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

	public override ActionResult RunKnownOperation(object target, string? operation)
	{
		switch (operation?.Trim())
		{
			case "Focus":
				return Focus(target);
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

				break;
			case "AcceptDialog":
			case "CancelDialog":
				if (target is Window window)
				{
					window.DialogResult = string.Equals(operation?.Trim(), "AcceptDialog", StringComparison.Ordinal);
					window.Close();
					return ActionResult.Ok();
				}

				break;
		}

		return base.RunKnownOperation(target, operation);
	}

	internal static object? GetFocusedTarget() =>
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
			case System.Windows.Controls.Primitives.TextBoxBase textBoxBase:
				textBoxBase.SelectAll();
				break;
			case ComboBox comboBox:
				comboBox.Text = string.Empty;
				break;
		}
	}

	private static bool ShouldHandleTextBoxKeyInternally(string keyText) =>
		TargetKeyboardInput.IsSelectAllShortcut(keyText)
		|| string.Equals(keyText, "Backspace", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Delete", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Del", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(keyText, "Space", StringComparison.OrdinalIgnoreCase)
		|| TargetKeyboardInput.IsPlainTextInputKey(keyText);

	private static ActionResult SendKeysToTextBox(TextBox textBox, string keyText)
	{
		if (TargetKeyboardInput.IsSelectAllShortcut(keyText))
		{
			textBox.SelectAll();
			return ActionResult.Ok();
		}

		if (string.Equals(keyText, "Backspace", StringComparison.OrdinalIgnoreCase))
		{
			DeleteTextBeforeCaret(textBox);
			return ActionResult.Ok();
		}

		if (string.Equals(keyText, "Delete", StringComparison.OrdinalIgnoreCase) || string.Equals(keyText, "Del", StringComparison.OrdinalIgnoreCase))
		{
			DeleteTextAtCaret(textBox);
			return ActionResult.Ok();
		}

		if (string.Equals(keyText, "Space", StringComparison.OrdinalIgnoreCase) || TargetKeyboardInput.IsPlainTextInputKey(keyText))
		{
			textBox.SelectedText = string.Equals(keyText, "Space", StringComparison.OrdinalIgnoreCase) ? " " : keyText;
			textBox.CaretIndex = textBox.Text.Length;
		}

		return ActionResult.Ok();
	}

	private static void DeleteTextBeforeCaret(TextBox textBox)
	{
		if (textBox.SelectionLength > 0)
		{
			var selectionStart = textBox.SelectionStart;
			textBox.SelectedText = string.Empty;
			textBox.CaretIndex = selectionStart;
			return;
		}

		if (textBox.CaretIndex <= 0 || textBox.Text.Length == 0)
			return;

		var removeIndex = textBox.CaretIndex - 1;
		textBox.Text = textBox.Text.Remove(removeIndex, 1);
		textBox.CaretIndex = removeIndex;
	}

	private static void DeleteTextAtCaret(TextBox textBox)
	{
		if (textBox.SelectionLength > 0)
		{
			var selectionStart = textBox.SelectionStart;
			textBox.SelectedText = string.Empty;
			textBox.CaretIndex = selectionStart;
			return;
		}

		if (textBox.CaretIndex >= textBox.Text.Length)
			return;

		var removeIndex = textBox.CaretIndex;
		textBox.Text = textBox.Text.Remove(removeIndex, 1);
		textBox.CaretIndex = removeIndex;
	}

	private static bool TryHandleMenuEscape(object target, string keyText)
	{
		if (!IsEscapeKey(keyText) || target is not MenuItem menuItem)
			return false;

		if (TryCloseDeepestOpenSubmenu(menuItem))
			return true;

		var current = LogicalTreeHelper.GetParent(menuItem) ?? VisualTreeHelper.GetParent(menuItem);
		while (current is not null)
		{
			if (current is MenuItem ancestor && ancestor.IsSubmenuOpen)
			{
				ancestor.IsSubmenuOpen = false;
				return true;
			}

			current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current);
		}

		return false;
	}

	private static bool IsEscapeKey(string keyText)
	{
		var normalized = keyText.Replace(" ", string.Empty);
		return string.Equals(normalized, "Escape", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(normalized, "Esc", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryCloseDeepestOpenSubmenu(MenuItem menuItem)
	{
		foreach (var child in menuItem.Items.OfType<MenuItem>())
			if (TryCloseDeepestOpenSubmenu(child))
				return true;

		if (!menuItem.IsSubmenuOpen)
			return false;

		menuItem.IsSubmenuOpen = false;
		return true;
	}

	private bool TryHandleFocusNavigation(object target, string keyText)
	{
		var direction = GetFocusNavigationDirection(keyText);
		if (direction is null)
			return false;

		Focus(target);
		var request = new TraversalRequest(direction.Value);
		return target switch
		{
			UIElement uiElement => uiElement.MoveFocus(request),
			ContentElement contentElement => contentElement.MoveFocus(request),
			_ => false,
		};
	}

	private static FocusNavigationDirection? GetFocusNavigationDirection(string keyText)
	{
		var normalized = keyText.Replace(" ", string.Empty);
		if (string.Equals(normalized, "Tab", StringComparison.OrdinalIgnoreCase))
			return FocusNavigationDirection.Next;
		if (string.Equals(normalized, "Shift+Tab", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(normalized, "LeftShift+Tab", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(normalized, "RightShift+Tab", StringComparison.OrdinalIgnoreCase))
		{
			return FocusNavigationDirection.Previous;
		}

		return null;
	}

	private static ActionResult PerformClick(UIElement target, MouseButtonKind button, int clickCount)
	{
		var mouseButton = ToWpfMouseButton(button);
		var count = Math.Max(1, clickCount);
		if (!target.IsEnabled)
			return ActionResult.Ok();

		UIHighlight.Select(target);
		TryEnsureAppHooks();
		using var syntheticMouseInput = AppHooks.BeginSyntheticMouseInput();
		if (TryGetScreenPoint(target, new PointerAnchor(0.5, 0.5), out var clickScreen, out _))
		{
			AppHooks.SetSyntheticMouseScreenPosition(clickScreen);
			VirtualPointerService.MoveTo(clickScreen, GetOwnerHwnd(target));
		}

		var clickEvent = mouseButton == MouseButton.Left ? ResolvePrimaryClickEvent(target) : null;
		var observedClickCount = 0;
		var observedDoubleClickCount = 0;
		RoutedEventHandler? clickObserver = null;
		MouseButtonEventHandler? doubleClickObserver = null;
		try
		{
			if (clickEvent is not null)
			{
				clickObserver = (_, _) => observedClickCount++;
				target.AddHandler(clickEvent, clickObserver, handledEventsToo: true);
			}

			if (count > 1 && mouseButton == MouseButton.Left && target is Control)
			{
				doubleClickObserver = (_, _) => observedDoubleClickCount++;
				target.AddHandler(Control.MouseDoubleClickEvent, doubleClickObserver, handledEventsToo: false);
			}

			var targets = GetAscendingVisualTree(target);
			for (var i = 0; i < count; i++)
			{
				var observedBeforeClick = observedClickCount;
				var observedBeforeDoubleClick = observedDoubleClickCount;
				AppHooks.SetButton(mouseButton, isPressed: true);
				RaiseMouseButtonEvent(target, UIElement.PreviewMouseDownEvent, mouseButton, targets);
				RaiseMouseButtonEvent(target, UIElement.MouseDownEvent, mouseButton, targets);

				AppHooks.SetButton(mouseButton, isPressed: false);
				RaiseMouseButtonEvent(target, UIElement.PreviewMouseUpEvent, mouseButton, targets);
				RaiseMouseButtonEvent(target, UIElement.MouseUpEvent, mouseButton, targets);
				VirtualPointerService.Click(button, 1);

				var observedDoubleClickDuringThisClick = observedDoubleClickCount != observedBeforeDoubleClick;
				if (clickEvent is not null && observedClickCount == observedBeforeClick && !observedDoubleClickDuringThisClick)
					target.RaiseEvent(new RoutedEventArgs(clickEvent, target));
			}

			if (count > 1 && mouseButton == MouseButton.Left)
				RaiseMouseButtonEvent(target, Control.MouseDoubleClickEvent, MouseButton.Left, targets, count);
			else if (count > 1)
				InvokeMouseGestureBindings(target, mouseButton, count);
		}
		finally
		{
			if (clickEvent is not null && clickObserver is not null)
				target.RemoveHandler(clickEvent, clickObserver);
			if (doubleClickObserver is not null)
				target.RemoveHandler(Control.MouseDoubleClickEvent, doubleClickObserver);
			AppHooks.ResetMouseState();
		}

		if (mouseButton == MouseButton.Right)
			OpenContextMenu(target);

		return ActionResult.Ok();
	}

	private static void ReportVirtualPointerForKnownRoutedEvent(object target, string eventName)
	{
		var normalized = eventName?.Trim();
		if (normalized is not ("Click" or "MouseDoubleClick"))
			return;
		if (target is not UIElement uiElement)
			return;
		if (!TryGetScreenPoint(uiElement, new PointerAnchor(0.5, 0.5), out var screen, out _))
			return;

		VirtualPointerService.MoveTo(screen, GetOwnerHwnd(uiElement));
		VirtualPointerService.Click(MouseButtonKind.Left, string.Equals(normalized, "MouseDoubleClick", StringComparison.Ordinal) ? 2 : 1);
	}

	private static MouseButton ToWpfMouseButton(MouseButtonKind button) =>
		button switch
		{
			MouseButtonKind.Right => MouseButton.Right,
			MouseButtonKind.Middle => MouseButton.Middle,
			_ => MouseButton.Left,
		};

	private static RoutedEvent? ResolvePrimaryClickEvent(UIElement target) =>
		target switch
		{
			ButtonBase => ButtonBase.ClickEvent,
			MenuItem => MenuItem.ClickEvent,
			_ => null,
		};

	private static void TryEnsureAppHooks()
	{
		try
		{
			AppHooks.EnsureHooked();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
		}
	}

	private static bool RaiseMouseButtonEvent(UIElement target, RoutedEvent routedEvent, MouseButton button, IReadOnlyList<UIElement> targets, int clickCount = 1)
	{
		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
		{
			RoutedEvent = routedEvent,
			Source = target,
		};
		SetMouseButtonClickCount(args, clickCount);
		if (routedEvent == Control.MouseDoubleClickEvent)
			InvokeMouseGestureBindings(target, button, clickCount);

		AppHooks.MouseOverElement?.SetValue(Mouse.PrimaryDevice, target);
		foreach (var hoveredTarget in targets)
			AppHooks.WriteElementOverElement?.Invoke(hoveredTarget, new object[] { AppHooks.CoreFlags.IsMouseOverCache, true });

		AppHooks.SetSyntheticMouseHitTarget(target);
		if (!TryRaiseTrustedEvent(target, args))
			target.RaiseEvent(args);
		return args.Handled;
	}

	private static bool RaiseMouseMoveEvent(UIElement target)
	{
		var args = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
		{
			RoutedEvent = UIElement.MouseMoveEvent,
			Source = target,
		};

		AppHooks.MouseOverElement?.SetValue(Mouse.PrimaryDevice, target);
		foreach (var hoveredTarget in GetAscendingVisualTree(target))
			AppHooks.WriteElementOverElement?.Invoke(hoveredTarget, new object[] { AppHooks.CoreFlags.IsMouseOverCache, true });

		AppHooks.SetSyntheticMouseHitTarget(target);
		if (!TryRaiseTrustedEvent(target, args))
			target.RaiseEvent(args);
		return args.Handled;
	}

	private static bool TryGetScreenPoint(UIElement target, PointerAnchor anchor, out Point screen, out string? error)
	{
		screen = default;
		error = null;
		var width = target.RenderSize.Width;
		var height = target.RenderSize.Height;
		if (!IsPositiveFinite(width) || !IsPositiveFinite(height))
		{
			error = "target has no renderable size.";
			return false;
		}

		try
		{
			screen = target.PointToScreen(new Point(width * anchor.X, height * anchor.Y));
			return true;
		}
		catch (InvalidOperationException ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static Point Interpolate(Point start, Point end, double progress) =>
		new(
			start.X + (end.X - start.X) * progress,
			start.Y + (end.Y - start.Y) * progress);

	private static bool TryRaiseTrustedEvent(UIElement target, RoutedEventArgs args)
	{
		var argsArray = new object[] { args };
		var method = typeof(UIElement)
			.GetMethods(InvokeCommandBindings)
			.Where(static method => method.Name == "RaiseTrustedEvent")
			.FirstOrDefault(method => ParametersMatch(method.GetParameters(), argsArray));
		if (method is null)
			return false;

		try
		{
			method.Invoke(target, argsArray);
			return true;
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			throw ex.InnerException;
		}
	}

	private static bool ParametersMatch(ParameterInfo[] parameterInfos, object[] args)
	{
		if (parameterInfos.Length != args.Length)
			return false;

		for (var i = 0; i < parameterInfos.Length; i++)
		{
			var parameterType = parameterInfos[i].ParameterType;
			if (!parameterType.IsAssignableFrom(args[i].GetType()))
				return false;
		}

		return true;
	}

	private static IReadOnlyList<UIElement> GetAscendingVisualTree(DependencyObject element)
	{
		List<DependencyObject> targets = [];
		DependencyObject? current = element;
		while (current is not null)
		{
			targets.Add(current);
			current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
		}

		return targets.OfType<UIElement>().ToArray();
	}

	private static void OpenContextMenu(UIElement target)
	{
		if (target is not DependencyObject dependencyObject)
			return;

		var contextMenuOpeningArgs = TryCreateContextMenuOpeningArgs(target);
		if (contextMenuOpeningArgs is not null)
			target.RaiseEvent(contextMenuOpeningArgs);

		var contextMenu = ContextMenuService.GetContextMenu(dependencyObject);
		if (contextMenu is null)
			return;

		contextMenu.PlacementTarget = target;
		contextMenu.Placement = PlacementMode.Bottom;
		contextMenu.IsOpen = true;
	}

	private static RoutedEventArgs? TryCreateContextMenuOpeningArgs(UIElement target)
	{
		var constructor = typeof(ContextMenuEventArgs).GetConstructor(
			BindingFlags.Instance | BindingFlags.NonPublic,
			binder: null,
			types: new[] { typeof(object), typeof(bool) },
			modifiers: null);
		if (constructor is null)
			return null;

		var args = (ContextMenuEventArgs)constructor.Invoke(new object[] { target, true });
		args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent;
		return args;
	}

	private static RoutedEventArgs CreateKnownRoutedEventArgs(string eventName, RoutedEvent routedEvent, object source)
	{
		if (string.Equals(eventName?.Trim(), "MouseDoubleClick", StringComparison.Ordinal))
		{
			var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
			{
				RoutedEvent = routedEvent,
				Source = source,
			};
			SetMouseButtonClickCount(args, 2);
			return args;
		}

		return new RoutedEventArgs(routedEvent, source);
	}

	private static void SetMouseButtonClickCount(MouseButtonEventArgs args, int clickCount)
	{
		var count = Math.Max(1, clickCount);
		var property = typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount), InvokeCommandBindings);
		if (property?.SetMethod is not null)
		{
			property.SetValue(args, count, null);
			return;
		}

		typeof(MouseButtonEventArgs).GetField("_count", InvokeCommandBindings)?.SetValue(args, count);
	}

	private static bool TryInvokeKeyGestureBindings(object target, string keyText)
	{
		var invoked = false;
		foreach (var group in KeyboardInput.ParseKeyGroups(keyText))
		{
			if (!TryGetKeyGestureParts(group, out var key, out var modifiers))
				continue;

			invoked |= InvokeMatchingCommandGestures(
				target,
				gesture => gesture is KeyGesture keyGesture
					&& keyGesture.Key == key
					&& keyGesture.Modifiers == modifiers);
		}

		return invoked;
	}

	private static bool InvokeMouseGestureBindings(object target, MouseButton button, int clickCount)
	{
		if (!TryGetMouseAction(button, clickCount, out var mouseAction))
			return false;

		return InvokeMatchingCommandGestures(
			target,
			gesture => gesture is MouseGesture mouseGesture
				&& mouseGesture.MouseAction == mouseAction
				&& mouseGesture.Modifiers == ModifierKeys.None);
	}

	private static bool InvokeMatchingCommandGestures(object target, Func<InputGesture, bool> matches)
	{
		var invoked = false;
		var executedCommands = new HashSet<ICommand>();
		foreach (var bindingOwner in EnumerateInputBindingOwners(target))
		{
			var inputBindings = GetInputBindings(bindingOwner);
			if (inputBindings is not null)
			{
				foreach (var candidate in inputBindings)
				{
					if (candidate is not InputBinding binding)
						continue;

					if (binding.Gesture is null || !matches(binding.Gesture))
						continue;

					invoked |= ExecuteInputBinding(binding, bindingOwner, target, executedCommands);
				}
			}

			var commandBindings = GetCommandBindings(bindingOwner);
			if (commandBindings is null)
				continue;

			foreach (var candidate in commandBindings)
			{
				if (candidate is not CommandBinding binding)
					continue;

				if (binding.Command is not RoutedCommand routedCommand)
					continue;
				if (!routedCommand.InputGestures.OfType<InputGesture>().Any(matches))
					continue;

				var commandTarget = target as IInputElement ?? bindingOwner as IInputElement;
				invoked |= ExecuteRoutedCommand(routedCommand, parameter: null, commandTarget, executedCommands);
			}
		}

		return invoked;
	}

	private static bool TryGetKeyGestureParts(IReadOnlyCollection<Key> keys, out Key key, out ModifierKeys modifiers)
	{
		key = Key.None;
		modifiers = ModifierKeys.None;
		foreach (var candidate in keys)
		{
			if (TryAddModifier(candidate, ref modifiers))
				continue;

			if (key != Key.None)
				return false;
			key = candidate;
		}

		return key != Key.None;
	}

	private static bool TryAddModifier(Key key, ref ModifierKeys modifiers)
	{
		switch (key)
		{
			case Key.LeftCtrl:
			case Key.RightCtrl:
				modifiers |= ModifierKeys.Control;
				return true;
			case Key.LeftAlt:
			case Key.RightAlt:
				modifiers |= ModifierKeys.Alt;
				return true;
			case Key.LeftShift:
			case Key.RightShift:
				modifiers |= ModifierKeys.Shift;
				return true;
			case Key.LWin:
			case Key.RWin:
				modifiers |= ModifierKeys.Windows;
				return true;
			default:
				return false;
		}
	}

	private static bool TryGetMouseAction(MouseButton button, int clickCount, out MouseAction mouseAction)
	{
		mouseAction = MouseAction.None;
		var isDoubleClick = clickCount > 1;
		switch (button)
		{
			case MouseButton.Left:
				mouseAction = isDoubleClick ? MouseAction.LeftDoubleClick : MouseAction.LeftClick;
				return true;
			case MouseButton.Right:
				mouseAction = isDoubleClick ? MouseAction.RightDoubleClick : MouseAction.RightClick;
				return true;
			case MouseButton.Middle:
				mouseAction = isDoubleClick ? MouseAction.MiddleDoubleClick : MouseAction.MiddleClick;
				return true;
			default:
				return false;
		}
	}

	private static IEnumerable<object> EnumerateInputBindingOwners(object target)
	{
		var current = target;
		while (current is not null)
		{
			yield return current;
			current = current is DependencyObject dependencyObject
				? VisualTreeHelper.GetParent(dependencyObject) ?? LogicalTreeHelper.GetParent(dependencyObject)
				: null;
		}
	}

	private static InputBindingCollection? GetInputBindings(object owner) =>
		owner switch
		{
			UIElement uiElement => uiElement.InputBindings,
			ContentElement contentElement => contentElement.InputBindings,
			_ => null,
		};

	private static CommandBindingCollection? GetCommandBindings(object owner) =>
		owner switch
		{
			UIElement uiElement => uiElement.CommandBindings,
			ContentElement contentElement => contentElement.CommandBindings,
			_ => null,
		};

	private static bool ExecuteInputBinding(InputBinding binding, object bindingOwner, object originalTarget, ISet<ICommand>? executedCommands)
	{
		var command = binding.Command;
		if (command is null)
			return false;

		var parameter = binding.CommandParameter;
		var commandTarget = binding.CommandTarget
			?? bindingOwner as IInputElement
			?? originalTarget as IInputElement;

		if (command is RoutedCommand routedCommand)
			return ExecuteRoutedCommand(routedCommand, parameter, commandTarget, executedCommands);

		if (command.CanExecute(parameter))
		{
			if (executedCommands is not null && !executedCommands.Add(command))
				return false;
			command.Execute(parameter);
			return true;
		}

		return false;
	}

	private static bool ExecuteRoutedCommand(RoutedCommand routedCommand, object? parameter, IInputElement? commandTarget, ISet<ICommand>? executedCommands)
	{
		if (commandTarget is null || !routedCommand.CanExecute(parameter, commandTarget))
			return false;
		if (executedCommands is not null && !executedCommands.Add(routedCommand))
			return false;

		routedCommand.Execute(parameter, commandTarget);
		return true;
	}

	private static RoutedEvent? ResolveRoutedEvent(Type targetType, string eventName)
	{
		var normalized = eventName?.Trim();
		if (!IsKnownRoutedEventAllowed(normalized))
			return null;

		for (var type = targetType; type is not null; type = type.BaseType)
		{
			var field = type.GetField($"{normalized}Event", RoutedEventBindings);
			if (field?.GetValue(null) is RoutedEvent routedEvent)
				return routedEvent;
		}

		return normalized switch
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

	private static bool IsKnownRoutedEventAllowed(string? eventName) =>
		eventName is "Click" or "MouseDoubleClick" or "Checked" or "Unchecked" or "Expanded" or "Collapsed";

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

	private static bool TrySetForegroundWindow(Window? window)
	{
		if (window is null)
			return false;

		var handle = new WindowInteropHelper(window).Handle;
		return handle != IntPtr.Zero && NativeMethods.SetForegroundWindow(handle);
	}

	private static IntPtr GetOwnerHwnd(UIElement element)
	{
		if (element is Window window)
			return new WindowInteropHelper(window).Handle;

		if (element is Visual visual && PresentationSource.FromVisual(visual) is HwndSource source)
			return source.Handle;

		var ownerWindow = Window.GetWindow(element);
		return ownerWindow is null ? IntPtr.Zero : new WindowInteropHelper(ownerWindow).Handle;
	}

	private static bool IsPositiveFinite(double value) =>
		value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
