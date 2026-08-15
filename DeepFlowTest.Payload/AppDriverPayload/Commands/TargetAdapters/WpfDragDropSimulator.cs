namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.AppDriverPayload.Diagnostics;

internal static class WpfDragDropSimulator
{
	public static ActionResult PerformInjected(
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

		if (!WpfPointerInput.TryGetScreenPoint(source, sourceAnchor, out var sourceScreen, out var error))
			return ActionResult.Unsupported($"WPF source target screen coordinates could not be resolved: {error}");
		if (!WpfPointerInput.TryGetScreenPoint(destination, destinationAnchor, out var destinationScreen, out error))
			return ActionResult.Unsupported($"WPF destination target screen coordinates could not be resolved: {error}");

		WpfPointerInput.TryEnsureAppHooks();
		using var syntheticMouseInput = AppHooks.BeginSyntheticMouseInput();
		try
		{
			AppHooks.SetSyntheticMouseScreenPosition(sourceScreen);
			VirtualPointerService.BeginDrag(sourceScreen, WpfWindowActivation.GetOwnerHwnd(source));
			AppHooks.SetButton(MouseButton.Left, isPressed: true);
			var sourceTargets = WpfPointerInput.GetAscendingVisualTree(source);
			WpfPointerInput.RaiseMouseButtonEvent(source, UIElement.PreviewMouseDownEvent, MouseButton.Left, sourceTargets);
			WpfPointerInput.RaiseDirectMouseButtonEventOnTargets(sourceTargets, UIElement.PreviewMouseLeftButtonDownEvent, MouseButton.Left, source);

			var steps = Math.Max(1, durationMs / Math.Max(1, stepIntervalMs));
			for (var i = 1; i <= steps; i++)
			{
				var progress = (double)i / steps;
				var currentScreen = Interpolate(sourceScreen, destinationScreen, progress);
				AppHooks.SetSyntheticMouseScreenPosition(currentScreen);
				VirtualPointerService.DragMove(currentScreen);
				WpfPointerInput.RaiseMouseMoveEvent(source);
				WpfPointerInput.RaiseDirectMouseMoveEventOnTargets(sourceTargets, source);
			}

			var hasSyntheticDragDrop = AppHooks.TryGetSyntheticDragDrop(out var dragDropData, out var allowedEffects);
			var destinationTargets = WpfPointerInput.GetAscendingVisualTree(destination);
			var destinationDropTargets = GetDragDropEventTargets(destinationTargets);
			if (hasSyntheticDragDrop)
			{
				if (!TryRaiseDragDropEventOnTargets(destinationDropTargets, DragDrop.DragEnterEvent, dragDropData, allowedEffects, destinationScreen, out var dragEnterError))
					return ActionResult.Unsupported(dragEnterError);
				if (!TryRaiseDragDropEventOnTargets(destinationDropTargets, DragDrop.DragOverEvent, dragDropData, allowedEffects, destinationScreen, out var dragOverError))
					return ActionResult.Unsupported(dragOverError);
			}

			AppHooks.SetSyntheticMouseScreenPosition(destinationScreen);
			VirtualPointerService.EndDrag(destinationScreen);
			WpfPointerInput.RaiseMouseMoveEvent(destination);
			if (hasSyntheticDragDrop && !TryRaiseDragDropEventOnTargets(destinationDropTargets, DragDrop.DropEvent, dragDropData, allowedEffects, destinationScreen, out var dropError))
				return ActionResult.Unsupported(dropError);
			AppHooks.SetButton(MouseButton.Left, isPressed: false);
			WpfPointerInput.RaiseMouseButtonEvent(destination, UIElement.PreviewMouseUpEvent, MouseButton.Left, destinationTargets);
			WpfPointerInput.RaiseDirectMouseButtonEventOnTargets(destinationTargets, UIElement.PreviewMouseLeftButtonUpEvent, MouseButton.Left, destination);
		}
		finally
		{
			AppHooks.ResetMouseState();
		}

		return ActionResult.Ok();
	}

	private static bool TryRaiseDragDropEventOnTargets(
		IReadOnlyList<UIElement> targets,
		RoutedEvent routedEvent,
		object data,
		DragDropEffects allowedEffects,
		Point screenPoint,
		out string error)
	{
		error = string.Empty;
		var raised = false;
		foreach (var target in targets)
		{
			Point targetPoint;
			try
			{
				targetPoint = target.PointFromScreen(screenPoint);
			}
			catch (InvalidOperationException ex)
			{
				error = ex.Message;
				continue;
			}

			var args = TryCreateDragEventArgs(data, allowedEffects, target, targetPoint, out error);
			if (args is null)
				return false;

			args.RoutedEvent = routedEvent;
			args.Source = target;
			target.RaiseEvent(args);
			raised = true;
			if (args.Handled)
				return true;
		}

		return raised;
	}

	private static IReadOnlyList<UIElement> GetDragDropEventTargets(IReadOnlyList<UIElement> targets)
	{
		var allowDropTargets = targets.Where(target => target.AllowDrop).ToArray();
		return allowDropTargets.Length == 0 ? targets : allowDropTargets;
	}

	private static DragEventArgs? TryCreateDragEventArgs(
		object data,
		DragDropEffects allowedEffects,
		DependencyObject target,
		Point targetPoint,
		out string error)
	{
		error = string.Empty;
		var constructor = typeof(DragEventArgs).GetConstructor(
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
			binder: null,
			types:
			[
				typeof(IDataObject),
				typeof(DragDropKeyStates),
				typeof(DragDropEffects),
				typeof(DependencyObject),
				typeof(Point),
			],
			modifiers: null);
		if (constructor is null)
		{
			error = "Could not find the WPF DragEventArgs constructor.";
			return null;
		}

		var dataObject = data as IDataObject ?? new DataObject(data);
		return (DragEventArgs)constructor.Invoke(
		[
			dataObject,
			DragDropKeyStates.LeftMouseButton,
			allowedEffects,
			target,
			targetPoint,
		]);
	}

	private static Point Interpolate(Point start, Point end, double progress) =>
		new(
			start.X + (end.X - start.X) * progress,
			start.Y + (end.Y - start.Y) * progress);
}
