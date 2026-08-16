namespace DeepFlowTest.Utility.WpfUtility.Tree;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using DeepFlowTest.Shared;

internal static class NativeDialogService
{
	private const int GwlStyle = -16;
	private const int WsCaption = 0x00C00000;
	private const int WsPopup = unchecked((int)0x80000000);
	private static readonly AsyncLocal<RootWindowOverrideScope?> rootWindowOverride = new();

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
		if (rootWindowOverride.Value is { } overrideScope)
			return overrideScope.Roots;

		var processId = Process.GetCurrentProcess().Id;
		List<IntPtr> windows = [];
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
		if (roots == null)
			throw new ArgumentNullException(nameof(roots));
		var scope = new RootWindowOverrideScope(roots.ToArray(), rootWindowOverride.Value);
		rootWindowOverride.Value = scope;
		return scope;
	}

	private sealed class RootWindowOverrideScope(
		IReadOnlyList<IntPtr> roots,
		RootWindowOverrideScope? parent) : IDisposable
	{
		private bool disposed;

		public IReadOnlyList<IntPtr> Roots { get; } = roots;

		public void Dispose()
		{
			if (disposed)
				return;
			if (!ReferenceEquals(rootWindowOverride.Value, this))
				throw new InvalidOperationException("Native dialog root overrides must be disposed in LIFO order within their logical context.");

			disposed = true;
			rootWindowOverride.Value = parent;
		}
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

}
