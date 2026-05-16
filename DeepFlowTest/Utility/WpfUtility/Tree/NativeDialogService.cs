namespace DeepFlowTest.Utility.WpfUtility.Tree;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using DeepFlowTest.Shared;

internal static class NativeDialogService
{
	private const int GwlStyle = -16;
	private const int WsCaption = 0x00C00000;
	private const int WsPopup = unchecked((int)0x80000000);
	private static IReadOnlyList<IntPtr>? rootWindowsForTests;

	public static bool HasRootWindowsForCurrentProcess() =>
		GetRootWindowsForCurrentProcess().Count != 0;

	public static TreeService? TryCreateTreeService()
	{
		var roots = GetRootWindowsForCurrentProcess();
		if (roots.Count == 0)
			return null;

		return new TreeService(rootProvider: () => roots.Cast<object>().ToArray());
	}

	public static IReadOnlyList<IntPtr> GetRootWindowsForCurrentProcess()
	{
		if (rootWindowsForTests is not null)
			return rootWindowsForTests;

		var processId = Process.GetCurrentProcess().Id;
		var windows = new List<IntPtr>();
		NativeMethods.EnumWindows((hwnd, _) =>
		{
			NativeMethods.GetWindowThreadProcessId(hwnd, out var windowProcessId);
			if (windowProcessId == processId && IsNativeDialogRoot(hwnd))
				windows.Add(hwnd);

			return true;
		}, IntPtr.Zero);

		return windows;
	}

	internal static IDisposable OverrideRootWindowsForTests(IReadOnlyList<IntPtr> roots)
	{
		var previous = rootWindowsForTests;
		rootWindowsForTests = roots.ToArray();
		return new RestoreRootWindows(previous);
	}

	private static bool IsNativeDialogRoot(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
			return false;

		var className = GetClassName(hwnd);
		if (string.IsNullOrWhiteSpace(className) || IsKnownManagedRootClass(className))
			return false;

		if (string.Equals(className, "#32770", StringComparison.Ordinal))
			return true;

		var style = NativeMethods.GetWindowLong(hwnd, GwlStyle);
		return (style & WsPopup) == WsPopup && (style & WsCaption) == WsCaption;
	}

	internal static string GetClassName(IntPtr hwnd)
	{
		var builder = new StringBuilder(256);
		return NativeMethods.GetClassName(hwnd, builder, builder.Capacity) == 0 ? string.Empty : builder.ToString();
	}

	internal static string GetWindowText(IntPtr hwnd)
	{
		var length = Math.Max(0, NativeMethods.GetWindowTextLength(hwnd));
		var builder = new StringBuilder(length + 1);
		return NativeMethods.GetWindowText(hwnd, builder, builder.Capacity) == 0 ? string.Empty : builder.ToString();
	}

	internal static bool IsVisible(IntPtr hwnd) =>
		hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd);

	internal static bool IsEnabled(IntPtr hwnd) =>
		hwnd != IntPtr.Zero && NativeMethods.IsWindowEnabled(hwnd);

	internal static int GetControlId(IntPtr hwnd) =>
		hwnd == IntPtr.Zero ? 0 : NativeMethods.GetDlgCtrlID(hwnd);

	private static bool IsKnownManagedRootClass(string className) =>
		className.StartsWith("HwndWrapper", StringComparison.Ordinal)
		|| className.StartsWith("WindowsForms", StringComparison.Ordinal);

	private sealed class RestoreRootWindows : IDisposable
	{
		private readonly IReadOnlyList<IntPtr>? previous;

		public RestoreRootWindows(IReadOnlyList<IntPtr>? previous)
		{
			this.previous = previous;
		}

		public void Dispose()
		{
			rootWindowsForTests = previous;
		}
	}
}
