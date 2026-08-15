namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DeepFlowTest.AppDriverPayload.Commands;

internal static class WpfRoutedEventInvoker
{
	private const BindingFlags RoutedEventBindings = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

	public static ActionResult RaiseKnown(object target, string eventName, Func<ActionResult> fallback)
	{
		if (target is not UIElement && target is not ContentElement)
			return fallback();

		var routedEvent = ResolveRoutedEvent(target.GetType(), eventName);
		if (routedEvent is null)
			return ActionResult.Unsupported($"Routed event '{eventName}' is not allow-listed.");

		var args = CreateKnownRoutedEventArgs(eventName, routedEvent, target);
		WpfPointerInput.ReportVirtualPointerForKnownRoutedEvent(target, eventName);
		if (args is MouseButtonEventArgs && string.Equals(eventName?.Trim(), "MouseDoubleClick", StringComparison.Ordinal))
			WpfPointerInput.InvokeMouseGestureBindings(target, MouseButton.Left, 2);

		Raise(target, args);
		return ActionResult.Ok();
	}

	public static ActionResult RaiseExpression(object target, object? expressionPayload, int? timeoutMs, Func<ActionResult> fallback)
	{
		if (target is not UIElement && target is not ContentElement)
			return fallback();

		var evaluated = TargetExpressionEvaluator.Evaluate(target, expressionPayload, timeoutMs, awaitTasks: false);
		if (evaluated is not RoutedEventArgs args)
			return ActionResult.Unsupported("Routed event expression did not return RoutedEventArgs.");
		if (args.RoutedEvent is null)
			return ActionResult.Unsupported("Routed event expression returned args without RoutedEvent.");

		args.Source ??= target;
		Raise(target, args);
		return ActionResult.Ok();
	}

	private static void Raise(object target, RoutedEventArgs args)
	{
		if (target is UIElement targetElement)
			targetElement.RaiseEvent(args);
		else
			((ContentElement)target).RaiseEvent(args);
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
			WpfPointerInput.SetMouseButtonClickCount(args, 2);
			return args;
		}

		return new RoutedEventArgs(routedEvent, source);
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
}
