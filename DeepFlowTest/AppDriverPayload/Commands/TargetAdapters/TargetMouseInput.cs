namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.AppDriverPayload.Diagnostics;
using DeepFlowTest.Shared;

internal sealed class DragPlan
{
	public DragPlan(
		PointerTarget source,
		PointerTarget destination,
		int durationMs,
		int holdMs,
		int stepIntervalMs,
		int postDropWaitMs,
		bool ensureForeground,
		bool validateSameProcess)
	{
		Source = source;
		Destination = destination;
		DurationMs = durationMs;
		HoldMs = holdMs;
		StepIntervalMs = stepIntervalMs;
		PostDropWaitMs = postDropWaitMs;
		EnsureForeground = ensureForeground;
		ValidateSameProcess = validateSameProcess;
	}

	public PointerTarget Source { get; }

	public PointerTarget Destination { get; }

	public int DurationMs { get; }

	public int HoldMs { get; }

	public int StepIntervalMs { get; }

	public int PostDropWaitMs { get; }

	public bool EnsureForeground { get; }

	public bool ValidateSameProcess { get; }
}

internal static class TargetMouseInput
{
	public static ActionResult PerformDragAndDrop(DragPlan plan, CancellationToken cancellationToken)
	{
		_ = plan ?? throw new ArgumentNullException(nameof(plan));

		var mouseDown = false;
		string? error = null;
		var releaseHwnd = IntPtr.Zero;
		var releasePoint = ToNativePoint(plan.Destination);
		try
		{
			AppHooks.ResetMouseState();
			var sourceHwnd = ResolveMessageTarget(plan.Source, "source");
			var destinationHwnd = ResolveMessageTarget(plan.Destination, "destination");

			if (plan.ValidateSameProcess)
			{
				if (!IsWindowInCurrentProcess(sourceHwnd))
					return ActionResult.Unsupported("Source window is not owned by the target process.");
				if (!IsWindowInCurrentProcess(destinationHwnd))
					return ActionResult.Unsupported("Destination window is not owned by the target process.");
			}

			var sourcePoint = ToNativePoint(plan.Source);
			releaseHwnd = sourceHwnd;
			releasePoint = sourcePoint;

			VirtualPointerService.BeginDrag(new Point(plan.Source.ScreenX, plan.Source.ScreenY), sourceHwnd);
			PostMouseMove(sourceHwnd, sourcePoint, leftButtonDown: false, "source");
			PostMouseButton(sourceHwnd, NativeMethods.WM_LBUTTONDOWN, NativeMethods.MK_LBUTTON, sourcePoint, "left button down");
			mouseDown = true;
			Wait(plan.HoldMs, cancellationToken);

			var thresholdPoint = GetThresholdPoint(plan);
			releasePoint = thresholdPoint;
			PostMouseMove(sourceHwnd, thresholdPoint, leftButtonDown: true, "drag threshold");
			VirtualPointerService.DragMove(new Point(thresholdPoint.X, thresholdPoint.Y));
			Wait(Math.Min(plan.StepIntervalMs, plan.HoldMs), cancellationToken);

			MoveAlongPath(thresholdPoint, sourceHwnd, destinationHwnd, plan, point =>
			{
				releaseHwnd = destinationHwnd;
				releasePoint = point;
			}, cancellationToken);

			VirtualPointerService.EndDrag(new Point(plan.Destination.ScreenX, plan.Destination.ScreenY));
		}
		catch (OperationCanceledException)
		{
			error = "Drag and drop was canceled.";
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			error = ex.Message;
		}
		finally
		{
			if (mouseDown)
			{
				try
				{
					if (releaseHwnd == IntPtr.Zero)
						releaseHwnd = ResolveMessageTarget(plan.Destination, "destination");

					PostMouseButton(releaseHwnd, NativeMethods.WM_LBUTTONUP, 0, releasePoint, "left button up");
				}
				catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
				{
					error ??= $"Mouse button release failed: {ex.Message}";
				}
			}

			AppHooks.ResetMouseState();
		}

		if (!string.IsNullOrWhiteSpace(error))
			return ActionResult.Unsupported(error!);

		try
		{
			Wait(plan.PostDropWaitMs, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return ActionResult.Unsupported("Post-drop wait was canceled.");
		}

		return ActionResult.Ok();
	}

	private static void MoveAlongPath(
		NativeMethods.NativePoint start,
		IntPtr sourceHwnd,
		IntPtr destinationHwnd,
		DragPlan plan,
		Action<NativeMethods.NativePoint> recordReleasePoint,
		CancellationToken cancellationToken)
	{
		if (plan.DurationMs <= 0)
		{
			var destination = ToNativePoint(plan.Destination);
			PostMouseMove(destinationHwnd, destination, leftButtonDown: true, "destination");
			VirtualPointerService.DragMove(new Point(destination.X, destination.Y));
			recordReleasePoint(destination);
			return;
		}

		var steps = Math.Max(1, plan.DurationMs / Math.Max(1, plan.StepIntervalMs));
		var sleepPerStep = Math.Max(1, plan.DurationMs / steps);
		for (var i = 1; i <= steps; i++)
		{
			var progress = (double)i / steps;
			var x = (int)Math.Round(start.X + (plan.Destination.ScreenX - start.X) * progress);
			var y = (int)Math.Round(start.Y + (plan.Destination.ScreenY - start.Y) * progress);
			var point = new NativeMethods.NativePoint { X = x, Y = y };
			var hwnd = i == steps ? destinationHwnd : sourceHwnd;
			PostMouseMove(hwnd, point, leftButtonDown: true, "drag move");
			VirtualPointerService.DragMove(new Point(x, y));
			recordReleasePoint(point);
			Wait(sleepPerStep, cancellationToken);
		}
	}

	private static NativeMethods.NativePoint GetThresholdPoint(DragPlan plan)
	{
		var thresholdX = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDRAG));
		var thresholdY = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDRAG));
		var dx = plan.Destination.ScreenX - plan.Source.ScreenX;
		var dy = plan.Destination.ScreenY - plan.Source.ScreenY;
		if (dx == 0 && dy == 0)
		{
			return new NativeMethods.NativePoint
			{
				X = plan.Source.ScreenX + thresholdX,
				Y = plan.Source.ScreenY,
			};
		}

		var length = Math.Sqrt(dx * dx + dy * dy);
		var offsetX = (int)Math.Round(dx / length * thresholdX);
		var offsetY = (int)Math.Round(dy / length * thresholdY);
		if (offsetX == 0 && offsetY == 0)
			offsetX = Math.Sign(dx == 0 ? 1 : dx);

		return new NativeMethods.NativePoint
		{
			X = plan.Source.ScreenX + offsetX,
			Y = plan.Source.ScreenY + offsetY,
		};
	}

	private static IntPtr ResolveMessageTarget(PointerTarget target, string role)
	{
		var hwnd = target.OwnerHwnd;
		if (hwnd == IntPtr.Zero)
			throw new InvalidOperationException($"{role} target does not expose a native window handle.");
		if (!NativeMethods.IsWindow(hwnd))
			throw new InvalidOperationException($"{role} target window handle is not valid.");
		if (!NativeMethods.IsWindowEnabled(hwnd))
			throw new InvalidOperationException($"{role} target window is not enabled.");

		return hwnd;
	}

	private static bool IsWindowInCurrentProcess(IntPtr hwnd)
	{
		NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
		return processId == Process.GetCurrentProcess().Id;
	}

	private static NativeMethods.NativePoint ToNativePoint(PointerTarget target) =>
		new() { X = target.ScreenX, Y = target.ScreenY };

	private static void PostMouseMove(IntPtr hwnd, NativeMethods.NativePoint screenPoint, bool leftButtonDown, string phase) =>
		PostMouseMessage(
			hwnd,
			NativeMethods.WM_MOUSEMOVE,
			leftButtonDown ? NativeMethods.MK_LBUTTON : 0,
			screenPoint,
			phase);

	private static void PostMouseButton(IntPtr hwnd, int message, int wParam, NativeMethods.NativePoint screenPoint, string phase) =>
		PostMouseMessage(hwnd, message, wParam, screenPoint, phase);

	private static void PostMouseMessage(IntPtr hwnd, int message, int wParam, NativeMethods.NativePoint screenPoint, string phase)
	{
		var clientPoint = screenPoint;
		if (!NativeMethods.ScreenToClient(hwnd, ref clientPoint))
			throw new InvalidOperationException($"Mouse coordinates could not be converted during {phase}.");

		if (!NativeMethods.PostMessage(hwnd, message, new IntPtr(wParam), PackPoint(clientPoint)))
			throw new InvalidOperationException($"Mouse message could not be posted during {phase}.");
	}

	private static IntPtr PackPoint(NativeMethods.NativePoint point) =>
		new(unchecked((point.Y & 0xffff) << 16 | (point.X & 0xffff)));

	private static void Wait(int milliseconds, CancellationToken cancellationToken)
	{
		if (milliseconds <= 0)
			return;

		if (cancellationToken.WaitHandle.WaitOne(milliseconds))
			throw new OperationCanceledException(cancellationToken);
	}
}
