namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Runtime.InteropServices;
using System.Text;

internal static partial class TargetActionCommand
{
	private static class NativeMethods
	{
		public const int WM_LBUTTONDOWN = 0x0201;
		public const int WM_LBUTTONUP = 0x0202;
		public const int WM_RBUTTONDOWN = 0x0204;
		public const int WM_RBUTTONUP = 0x0205;
		public const int WM_SETTEXT = 0x000C;
		public const int BM_CLICK = 0x00F5;

		[DllImport("user32.dll")]
		public static extern bool SetForegroundWindow(IntPtr hwnd);

		[DllImport("user32.dll")]
		public static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

		[DllImport("user32.dll")]
		public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetWindowTextLength(IntPtr hwnd);

		[DllImport("user32.dll")]
		public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		public static extern int GetDlgCtrlID(IntPtr hwnd);

		[DllImport("user32.dll")]
		public static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildWindowsProc lpEnumFunc, IntPtr lParam);
	}

	private delegate bool EnumChildWindowsProc(IntPtr hwnd, IntPtr lParam);

	private struct NativeRect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}
}
