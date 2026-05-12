namespace DeepFlowTest.Utility.WpfUtility.Tree;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

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
		EnumWindows((hwnd, _) =>
		{
			GetWindowThreadProcessId(hwnd, out var windowProcessId);
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
		if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd))
			return false;

		var className = GetClassName(hwnd);
		if (string.IsNullOrWhiteSpace(className) || IsKnownManagedRootClass(className))
			return false;

		if (string.Equals(className, "#32770", StringComparison.Ordinal))
			return true;

		var style = GetWindowLong(hwnd, GwlStyle);
		return (style & WsPopup) == WsPopup && (style & WsCaption) == WsCaption;
	}

	internal static string GetClassName(IntPtr hwnd)
	{
		var builder = new StringBuilder(256);
		return GetClassName(hwnd, builder, builder.Capacity) == 0 ? string.Empty : builder.ToString();
	}

	internal static string GetWindowText(IntPtr hwnd)
	{
		var length = Math.Max(0, GetWindowTextLength(hwnd));
		var builder = new StringBuilder(length + 1);
		return GetWindowText(hwnd, builder, builder.Capacity) == 0 ? string.Empty : builder.ToString();
	}

	internal static bool IsVisible(IntPtr hwnd) =>
		hwnd != IntPtr.Zero && IsWindowVisible(hwnd);

	internal static bool IsEnabled(IntPtr hwnd) =>
		hwnd != IntPtr.Zero && IsWindowEnabled(hwnd);

	internal static int GetControlId(IntPtr hwnd) =>
		hwnd == IntPtr.Zero ? 0 : GetDlgCtrlID(hwnd);

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

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowTextLength(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindowEnabled(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	private static extern int GetDlgCtrlID(IntPtr hWnd);

	private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
}
