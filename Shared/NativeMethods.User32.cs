namespace DeepFlowTest.Shared;

using System;
using System.Runtime.InteropServices;
using System.Text;

internal static partial class NativeMethods
{
	public const int INPUT_KEYBOARD = 1;
	public const ushort VK_SHIFT = 0x10;
	public const ushort VK_CONTROL = 0x11;
	public const ushort VK_MENU = 0x12;
	public const uint KEYEVENTF_KEYUP = 0x0002;
	public const uint KEYEVENTF_UNICODE = 0x0004;

	public const int WM_LBUTTONDOWN = 0x0201;
	public const int WM_LBUTTONUP = 0x0202;
	public const int WM_RBUTTONDOWN = 0x0204;
	public const int WM_RBUTTONUP = 0x0205;
	public const int WM_MBUTTONDOWN = 0x0207;
	public const int WM_MBUTTONUP = 0x0208;
	public const int WM_SETTEXT = 0x000C;
	public const int BM_CLICK = 0x00F5;
	public const uint PW_RENDERFULLCONTENT = 0x00000002;

	[DllImport("user32.dll", SetLastError = true)]
	public static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

	[DllImport("user32.dll")]
	public static extern IntPtr GetMessageExtraInfo();

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern short VkKeyScan(char character);

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
	public static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildWindowsProc enumFunc, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

	[DllImport("user32.dll")]
	public static extern bool IsWindow(IntPtr hwnd);

	[DllImport("user32.dll")]
	public static extern bool IsWindowVisible(IntPtr hwnd);

	[DllImport("user32.dll")]
	public static extern bool IsWindowEnabled(IntPtr hwnd);

	[DllImport("user32.dll")]
	public static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	public static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);

	public delegate bool EnumChildWindowsProc(IntPtr hwnd, IntPtr lParam);

	public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential)]
	public struct Input
	{
		public int Type;
		public InputUnion Union;

		public static Input Keyboard(KeyboardInputData input) =>
			new()
			{
				Type = INPUT_KEYBOARD,
				Union = new InputUnion { Keyboard = input },
			};
	}

	[StructLayout(LayoutKind.Explicit)]
	public struct InputUnion
	{
		[FieldOffset(0)]
		public KeyboardInputData Keyboard;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct KeyboardInputData
	{
		public ushort VirtualKey;
		public ushort Scan;
		public uint Flags;
		public uint Time;
		public IntPtr ExtraInfo;
	}

	public struct NativeRect
	{
#pragma warning disable CS0649
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
#pragma warning restore CS0649
	}
}
