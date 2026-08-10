namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;

internal readonly struct PointerAnchor
{
	public PointerAnchor(double x, double y)
	{
		X = x;
		Y = y;
	}

	public double X { get; }

	public double Y { get; }
}

internal sealed class PointerTargetResult
{
	public bool Success { get; set; }

	public string? Error { get; set; }

	public PointerTarget? Value { get; set; }

	public static PointerTargetResult FromTarget(PointerTarget target) =>
		new() { Success = true, Value = target };

	public static PointerTargetResult Unsupported(string error) =>
		new() { Success = false, Error = error };
}

internal sealed class PointerTarget
{
	public PointerTarget(int screenX, int screenY, IntPtr ownerHwnd, string description)
	{
		ScreenX = screenX;
		ScreenY = screenY;
		OwnerHwnd = ownerHwnd;
		Description = description;
	}

	public int ScreenX { get; }

	public int ScreenY { get; }

	public IntPtr OwnerHwnd { get; }

	public string Description { get; }
}
