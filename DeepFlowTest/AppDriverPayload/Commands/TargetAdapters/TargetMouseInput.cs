namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
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
		try
		{
			AppHooks.ResetMouseState();
			VirtualPointerService.Hide();
			if (plan.EnsureForeground && plan.Source.OwnerHwnd != IntPtr.Zero)
				NativeMethods.SetForegroundWindow(plan.Source.OwnerHwnd);

			MoveCursor(plan.Source.ScreenX, plan.Source.ScreenY, "source");
			if (plan.ValidateSameProcess && !IsPointInCurrentProcess(plan.Source))
				return ActionResult.Unsupported("Source point is not over a window owned by the target process.");

			SendMouseButton(NativeMethods.MOUSEEVENTF_LEFTDOWN, "left button down");
			mouseDown = true;
			Wait(plan.HoldMs, cancellationToken);

			var thresholdPoint = GetThresholdPoint(plan);
			MoveCursor(thresholdPoint.X, thresholdPoint.Y, "drag threshold");
			Wait(Math.Min(plan.StepIntervalMs, plan.HoldMs), cancellationToken);

			MoveAlongPath(thresholdPoint, plan, cancellationToken);

			if (plan.ValidateSameProcess && !IsPointInCurrentProcess(plan.Destination))
				error = "Destination point is not over a window owned by the target process.";
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
					SendMouseButton(NativeMethods.MOUSEEVENTF_LEFTUP, "left button up");
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

	private static void MoveAlongPath(NativeMethods.NativePoint start, DragPlan plan, CancellationToken cancellationToken)
	{
		if (plan.DurationMs <= 0)
		{
			MoveCursor(plan.Destination.ScreenX, plan.Destination.ScreenY, "destination");
			return;
		}

		var steps = Math.Max(1, plan.DurationMs / Math.Max(1, plan.StepIntervalMs));
		var sleepPerStep = Math.Max(1, plan.DurationMs / steps);
		for (var i = 1; i <= steps; i++)
		{
			var progress = (double)i / steps;
			var x = (int)Math.Round(start.X + (plan.Destination.ScreenX - start.X) * progress);
			var y = (int)Math.Round(start.Y + (plan.Destination.ScreenY - start.Y) * progress);
			MoveCursor(x, y, "drag move");
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

	private static bool IsPointInCurrentProcess(PointerTarget target)
	{
		var hwnd = NativeMethods.WindowFromPoint(new NativeMethods.NativePoint { X = target.ScreenX, Y = target.ScreenY });
		if (hwnd == IntPtr.Zero)
			return false;

		NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
		return processId == Process.GetCurrentProcess().Id;
	}

	private static void MoveCursor(int x, int y, string phase)
	{
		if (!NativeMethods.SetCursorPos(x, y))
			throw new InvalidOperationException($"Mouse move failed during {phase}.");
	}

	private static void SendMouseButton(uint flags, string phase)
	{
		var input = NativeMethods.Input.Mouse(new NativeMethods.MouseInputData
		{
			Flags = flags,
			ExtraInfo = NativeMethods.GetMessageExtraInfo(),
		});
		var sent = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeMethods.Input)));
		if (sent != 1)
			throw new InvalidOperationException($"SendInput failed during {phase}.");
	}

	private static void Wait(int milliseconds, CancellationToken cancellationToken)
	{
		if (milliseconds <= 0)
			return;

		if (cancellationToken.WaitHandle.WaitOne(milliseconds))
			throw new OperationCanceledException(cancellationToken);
	}
}
