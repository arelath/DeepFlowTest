namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.AppDriverPayload.Diagnostics;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility.WpfUtility.SelectionHighlight;

internal static class WpfPointerInput
{
	private const BindingFlags EventArgumentBindings = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

	public static ActionResult Click(UIElement target, MouseButtonKind button, int clickCount)
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
			VirtualPointerService.MoveTo(clickScreen, WpfWindowActivation.GetOwnerHwnd(target));
		}

		var clickEvent = mouseButton == MouseButton.Left ? ResolvePrimaryClickEvent(target) : null;
		var observedClickCount = 0;
		var observedDoubleClickCount = 0;
		RoutedEventHandler? clickObserver = null;
		MouseButtonEventHandler? doubleClickObserver = null;
		ContextMenuEventHandler? contextMenuObserver = null;
		var observedContextMenuOpeningCount = 0;
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
				target.AddHandler(Control.MouseDoubleClickEvent, doubleClickObserver, handledEventsToo: true);
			}

			if (mouseButton == MouseButton.Right)
			{
				contextMenuObserver = (_, _) => observedContextMenuOpeningCount++;
				target.AddHandler(FrameworkElement.ContextMenuOpeningEvent, contextMenuObserver, handledEventsToo: true);
			}

			var targets = GetAscendingVisualTree(target);
			var suppressedMouseBindings = SuppressMouseBindingsForButton(target, mouseButton);
			try
			{
				for (var i = 0; i < count; i++)
				{
					var observedBeforeClick = observedClickCount;
					var observedBeforeDoubleClick = observedDoubleClickCount;
					AppHooks.SetButton(mouseButton, isPressed: true);
					RaiseMouseButtonEvent(target, UIElement.PreviewMouseDownEvent, mouseButton, targets);

					AppHooks.SetButton(mouseButton, isPressed: false);
					RaiseMouseButtonEvent(target, UIElement.PreviewMouseUpEvent, mouseButton, targets);
					VirtualPointerService.Click(button, 1);

					var menuHeaderHandled = mouseButton == MouseButton.Left && TryHandleMenuHeaderClick(target);
					var observedDoubleClickDuringThisClick = observedDoubleClickCount != observedBeforeDoubleClick;
					if (clickEvent is not null && observedClickCount == observedBeforeClick && !observedDoubleClickDuringThisClick && !menuHeaderHandled)
						target.RaiseEvent(new RoutedEventArgs(clickEvent, target));
				}
			}
			finally
			{
				suppressedMouseBindings.Dispose();
			}

			if (count == 1)
				InvokeMouseGestureBindings(target, mouseButton, 1);
			else if (mouseButton == MouseButton.Left && observedDoubleClickCount == 0)
				RaiseMouseButtonEvent(target, Control.MouseDoubleClickEvent, MouseButton.Left, targets, count);
			else if (mouseButton != MouseButton.Left)
				InvokeMouseGestureBindings(target, mouseButton, count);
		}
		finally
		{
			if (clickEvent is not null && clickObserver is not null)
				target.RemoveHandler(clickEvent, clickObserver);
			if (doubleClickObserver is not null)
				target.RemoveHandler(Control.MouseDoubleClickEvent, doubleClickObserver);
			if (contextMenuObserver is not null)
				target.RemoveHandler(FrameworkElement.ContextMenuOpeningEvent, contextMenuObserver);
			AppHooks.ResetMouseState();
		}

		if (mouseButton == MouseButton.Right && observedContextMenuOpeningCount == 0)
			OpenContextMenu(target);

		return ActionResult.Ok();
	}

	public static ActionResult MouseWheel(UIElement target, int delta)
	{
		if (!target.IsEnabled)
			return ActionResult.Ok();

		UIHighlight.Select(target);
		TryEnsureAppHooks();
		using var syntheticMouseInput = AppHooks.BeginSyntheticMouseInput();
		if (TryGetScreenPoint(target, new PointerAnchor(0.5, 0.5), out var wheelScreen, out _))
		{
			AppHooks.SetSyntheticMouseScreenPosition(wheelScreen);
			VirtualPointerService.MoveTo(wheelScreen, WpfWindowActivation.GetOwnerHwnd(target));
		}

		RaiseMouseWheelEvent(target, delta, GetAscendingVisualTree(target));
		return ActionResult.Ok();
	}

	public static PointerTargetResult GetPointerTarget(UIElement target, PointerAnchor anchor)
	{
		if (!target.IsVisible)
			return PointerTargetResult.Unsupported("WPF target is not visible.");
		if (!target.IsEnabled)
			return PointerTargetResult.Unsupported("WPF target is not enabled.");

		var width = target.RenderSize.Width;
		var height = target.RenderSize.Height;
		if (!IsPositiveFinite(width) || !IsPositiveFinite(height))
			return PointerTargetResult.Unsupported("WPF target has no renderable size.");

		try
		{
			var local = new Point(width * anchor.X, height * anchor.Y);
			var screen = target.PointToScreen(local);
			return PointerTargetResult.FromTarget(new PointerTarget(
				(int)Math.Round(screen.X),
				(int)Math.Round(screen.Y),
				WpfWindowActivation.GetOwnerHwnd(target),
				target.GetType().FullName ?? target.GetType().Name));
		}
		catch (InvalidOperationException ex)
		{
			return PointerTargetResult.Unsupported($"WPF target screen coordinates could not be resolved: {ex.Message}");
		}
	}

	internal static void ReportVirtualPointerClick(UIElement target)
	{
		if (!TryGetScreenPoint(target, new PointerAnchor(0.5, 0.5), out var clickScreen, out _))
			return;

		VirtualPointerService.MoveTo(clickScreen, WpfWindowActivation.GetOwnerHwnd(target));
		VirtualPointerService.Click(MouseButtonKind.Left, 1);
	}

	internal static void ReportVirtualPointerForKnownRoutedEvent(object target, string eventName)
	{
		var normalized = eventName?.Trim();
		if (normalized is not ("Click" or "MouseDoubleClick"))
			return;
		if (target is not UIElement uiElement)
			return;
		if (!TryGetScreenPoint(uiElement, new PointerAnchor(0.5, 0.5), out var screen, out _))
			return;

		VirtualPointerService.MoveTo(screen, WpfWindowActivation.GetOwnerHwnd(uiElement));
		VirtualPointerService.Click(MouseButtonKind.Left, string.Equals(normalized, "MouseDoubleClick", StringComparison.Ordinal) ? 2 : 1);
	}

	internal static void TryEnsureAppHooks()
	{
		try
		{
			AppHooks.EnsureHooked();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
		}
	}

	internal static bool RaiseMouseButtonEvent(
		UIElement target,
		RoutedEvent routedEvent,
		MouseButton button,
		IReadOnlyList<UIElement> targets,
		int clickCount = 1)
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
		InputManager.Current.ProcessInput(args);
		return args.Handled;
	}

	internal static bool RaiseMouseMoveEvent(UIElement target)
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
		InputManager.Current.ProcessInput(args);
		return args.Handled;
	}

	internal static bool RaiseDirectMouseButtonEventOnTargets(
		IReadOnlyList<UIElement> targets,
		RoutedEvent routedEvent,
		MouseButton button,
		UIElement hitTarget)
	{
		var handled = false;
		foreach (var target in targets)
		{
			var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
			{
				RoutedEvent = routedEvent,
				Source = target,
			};
			AppHooks.MouseOverElement?.SetValue(Mouse.PrimaryDevice, target);
			AppHooks.SetSyntheticMouseHitTarget(hitTarget);
			target.RaiseEvent(args);
			handled |= args.Handled;
		}

		return handled;
	}

	internal static bool RaiseDirectMouseMoveEventOnTargets(IReadOnlyList<UIElement> targets, UIElement hitTarget)
	{
		var handled = false;
		foreach (var target in targets)
		{
			var args = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
			{
				RoutedEvent = UIElement.MouseMoveEvent,
				Source = target,
			};
			AppHooks.MouseOverElement?.SetValue(Mouse.PrimaryDevice, target);
			AppHooks.SetSyntheticMouseHitTarget(hitTarget);
			target.RaiseEvent(args);
			handled |= args.Handled;
		}

		return handled;
	}

	internal static bool TryGetScreenPoint(UIElement target, PointerAnchor anchor, out Point screen, out string? error)
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

	internal static IReadOnlyList<UIElement> GetAscendingVisualTree(DependencyObject element)
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

	internal static void SetMouseButtonClickCount(MouseButtonEventArgs args, int clickCount)
	{
		var count = Math.Max(1, clickCount);
		var property = typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount), EventArgumentBindings);
		if (property?.SetMethod is not null)
		{
			property.SetValue(args, count, null);
			return;
		}

		typeof(MouseButtonEventArgs).GetField("_count", EventArgumentBindings)?.SetValue(args, count);
	}

	internal static bool InvokeMouseGestureBindings(object target, MouseButton button, int clickCount)
	{
		if (!TryGetMouseAction(button, clickCount, out var mouseAction))
			return false;

		return InvokeMatchingCommandGestures(
			target,
			gesture => gesture is MouseGesture mouseGesture
				&& mouseGesture.MouseAction == mouseAction
				&& mouseGesture.Modifiers == ModifierKeys.None);
	}

	private static bool RaiseMouseWheelEvent(UIElement target, int delta, IReadOnlyList<UIElement> targets)
	{
		var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
		{
			RoutedEvent = UIElement.PreviewMouseWheelEvent,
			Source = target,
		};

		AppHooks.MouseOverElement?.SetValue(Mouse.PrimaryDevice, target);
		foreach (var hoveredTarget in targets)
			AppHooks.WriteElementOverElement?.Invoke(hoveredTarget, new object[] { AppHooks.CoreFlags.IsMouseOverCache, true });

		AppHooks.SetSyntheticMouseHitTarget(target);
		InputManager.Current.ProcessInput(args);
		return args.Handled;
	}

	private static bool TryHandleMenuHeaderClick(UIElement target)
	{
		if (target is not MenuItem menuItem || menuItem.Items.Count == 0)
			return false;

		menuItem.IsSubmenuOpen = true;
		menuItem.Dispatcher.BeginInvoke(
			DispatcherPriority.Background,
			new Action(() => menuItem.IsSubmenuOpen = true));
		return true;
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

	private static void OpenContextMenu(UIElement target)
	{
		var contextMenuOpeningArgs = TryCreateContextMenuOpeningArgs(target);
		if (contextMenuOpeningArgs is not null)
			target.RaiseEvent(contextMenuOpeningArgs);

		foreach (var candidate in GetAscendingVisualTree(target))
		{
			var contextMenu = ContextMenuService.GetContextMenu(candidate);
			if (contextMenu is null)
				continue;

			contextMenu.PlacementTarget = candidate;
			contextMenu.Placement = PlacementMode.Bottom;
			contextMenu.IsOpen = true;
			return;
		}
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

	private static IDisposable SuppressMouseBindingsForButton(object target, MouseButton button)
	{
		List<SuppressedInputBinding> suppressed = [];
		foreach (var bindingOwner in EnumerateInputBindingOwners(target))
		{
			var inputBindings = GetInputBindings(bindingOwner);
			if (inputBindings is null)
				continue;

			for (var i = inputBindings.Count - 1; i >= 0; i--)
			{
				if (inputBindings[i] is not InputBinding binding)
					continue;
				if (binding.Gesture is not MouseGesture mouseGesture)
					continue;
				if (!IsMouseGestureForButton(mouseGesture, button))
					continue;

				inputBindings.RemoveAt(i);
				suppressed.Add(new SuppressedInputBinding(inputBindings, i, binding));
			}
		}

		return new SuppressedInputBindingScope(suppressed);
	}

	private static bool IsMouseGestureForButton(MouseGesture gesture, MouseButton button) =>
		button switch
		{
			MouseButton.Left => gesture.MouseAction is MouseAction.LeftClick or MouseAction.LeftDoubleClick,
			MouseButton.Right => gesture.MouseAction is MouseAction.RightClick or MouseAction.RightDoubleClick,
			MouseButton.Middle => gesture.MouseAction is MouseAction.MiddleClick or MouseAction.MiddleDoubleClick,
			_ => false,
		};

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

	private sealed class SuppressedInputBindingScope(IReadOnlyList<SuppressedInputBinding> suppressed) : IDisposable
	{
		private bool disposed;

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			foreach (var item in suppressed.OrderBy(static item => item.Index))
				item.Bindings.Insert(Math.Min(item.Index, item.Bindings.Count), item.Binding);
		}
	}

	private sealed class SuppressedInputBinding(InputBindingCollection bindings, int index, InputBinding binding)
	{
		public InputBindingCollection Bindings { get; } = bindings;

		public int Index { get; } = index;

		public InputBinding Binding { get; } = binding;
	}

	private static bool IsPositiveFinite(double value) =>
		value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
