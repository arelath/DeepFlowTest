namespace DeepFlowTest.Shared;

using System;
using System.Runtime.InteropServices;
using System.Text;

internal static partial class NativeMethods
{
	public const int INPUT_MOUSE = 0;
	public const int INPUT_KEYBOARD = 1;
	public const ushort VK_SHIFT = 0x10;
	public const ushort VK_CONTROL = 0x11;
	public const ushort VK_MENU = 0x12;
	public const ushort VK_LSHIFT = 0xA0;
	public const ushort VK_RSHIFT = 0xA1;
	public const ushort VK_LCONTROL = 0xA2;
	public const ushort VK_RCONTROL = 0xA3;
	public const ushort VK_LMENU = 0xA4;
	public const ushort VK_RMENU = 0xA5;
	public const uint KEYEVENTF_KEYUP = 0x0002;
	public const uint KEYEVENTF_UNICODE = 0x0004;
	public const int GWL_EXSTYLE = -20;
	public const int WS_EX_TRANSPARENT = 0x00000020;
	public const int WS_EX_TOOLWINDOW = 0x00000080;
	public const int WS_EX_NOACTIVATE = 0x08000000;
	public const int SM_CXDRAG = 68;
	public const int SM_CYDRAG = 69;

	public const int WM_KEYDOWN = 0x0100;
	public const int WM_KEYUP = 0x0101;
	public const int WM_CHAR = 0x0102;
	public const int WM_SYSKEYDOWN = 0x0104;
	public const int WM_SYSKEYUP = 0x0105;
	public const int WM_MOUSEMOVE = 0x0200;
	public const int WM_LBUTTONDOWN = 0x0201;
	public const int WM_LBUTTONUP = 0x0202;
	public const int WM_RBUTTONDOWN = 0x0204;
	public const int WM_RBUTTONUP = 0x0205;
	public const int WM_MBUTTONDOWN = 0x0207;
	public const int WM_MBUTTONUP = 0x0208;
	public const int MK_LBUTTON = 0x0001;
	public const int MK_RBUTTON = 0x0002;
	public const int MK_MBUTTON = 0x0010;
	public const int WM_SETTEXT = 0x000C;
	public const int BM_CLICK = 0x00F5;
	public const uint MAPVK_VK_TO_VSC = 0;
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
	public static extern bool GetCursorPos(out NativePoint point);

	[DllImport("user32.dll")]
	public static extern IntPtr WindowFromPoint(NativePoint point);

	[DllImport("user32.dll")]
	public static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

	[DllImport("user32.dll")]
	public static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);

	[DllImport("user32.dll")]
	public static extern int GetSystemMetrics(int index);

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
	public static extern uint MapVirtualKey(uint code, uint mapType);

	[DllImport("user32.dll")]
	public static extern bool GetKeyboardState(byte[] keyState);

	[DllImport("user32.dll")]
	public static extern bool SetKeyboardState(byte[] keyState);

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

	public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) =>
		IntPtr.Size == 8
			? GetWindowLongPtr64(hwnd, index)
			: new IntPtr(GetWindowLong32(hwnd, index));

	public static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) =>
		IntPtr.Size == 8
			? SetWindowLongPtr64(hwnd, index, value)
			: new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));

	[DllImport("user32.dll")]
	public static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);

	public delegate bool EnumChildWindowsProc(IntPtr hwnd, IntPtr lParam);

	public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

	[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
	private static extern int GetWindowLong32(IntPtr hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
	private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
	private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
	private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

	[StructLayout(LayoutKind.Sequential)]
	public struct Input
	{
		public int Type;
		public InputUnion Union;

		public static Input Mouse(MouseInputData input) =>
			new()
			{
				Type = INPUT_MOUSE,
				Union = new InputUnion { Mouse = input },
			};

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

		[FieldOffset(0)]
		public MouseInputData Mouse;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MouseInputData
	{
		public int Dx;
		public int Dy;
		public uint MouseData;
		public uint Flags;
		public uint Time;
		public IntPtr ExtraInfo;
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

	public struct NativePoint
	{
#pragma warning disable CS0649
		public int X;
		public int Y;
#pragma warning restore CS0649
	}
}
