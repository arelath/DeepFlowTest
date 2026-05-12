namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using DeepFlowTest.Contracts;
using WpfKey = System.Windows.Input.Key;
using WpfKeyInterop = System.Windows.Input.KeyInterop;

public sealed class Keyboard
{
	private readonly AppDriver driver;

	public Keyboard(AppDriver driver)
	{
		this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
	}

	public int DelayMs { get; set; } = 50;

	public bool EnsureForeground { get; set; } = true;

	public void Press(params WpfKey[] keys)
	{
		if (keys is null || keys.Length == 0)
			throw new ArgumentException("At least one key is required.", nameof(keys));

		var heldModifiers = new List<WpfKey>();
		try
		{
			foreach (var key in keys)
			{
				if (IsModifierKey(key))
				{
					if (!heldModifiers.Contains(key))
					{
						SendVirtualKey(key, isKeyDown: true);
						heldModifiers.Add(key);
					}

					continue;
				}

				SendVirtualKey(key, isKeyDown: true);
				SendVirtualKey(key, isKeyDown: false);
				if (DelayMs > 0)
					Thread.Sleep(DelayMs);
			}
		}
		finally
		{
			for (var i = heldModifiers.Count - 1; i >= 0; i--)
				SendVirtualKey(heldModifiers[i], isKeyDown: false);
		}
	}

	public void Type(string text)
	{
		foreach (var character in text ?? string.Empty)
		{
			var virtualKeyCode = NativeMethods.VkKeyScan(character);
			if (virtualKeyCode == -1)
			{
				SendUnicode(character, isKeyDown: true);
				SendUnicode(character, isKeyDown: false);
				continue;
			}

			var shiftState = (byte)((virtualKeyCode >> 8) & 0xff);
			var keyCode = (ushort)(virtualKeyCode & 0xff);
			var modifiers = PressShiftState(shiftState);
			try
			{
				SendKeyInput(keyCode, isKeyDown: true);
				SendKeyInput(keyCode, isKeyDown: false);
			}
			finally
			{
				ReleaseShiftState(modifiers);
			}

			if (DelayMs > 0)
				Thread.Sleep(Math.Min(DelayMs, 50));
		}
	}

	public void Type(Element element, string text, bool clearFirst = false)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		element.Type(text, clearFirst);
	}

	public void Press(Element element, string key)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		SendKey(element.TargetId, key);
	}

	public void Shortcut(Element element, params string[] keys)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		if (keys is null || keys.Length == 0)
			throw new ArgumentException("At least one key is required.", nameof(keys));

		SendKey(element.TargetId, string.Join("+", keys.Where(static key => string.IsNullOrWhiteSpace(key) == false)));
	}

	private void SendKey(string targetId, string keys)
	{
		var response = driver.Send<StandardIpcResponse>(new KeyPressCommandRequest
		{
			TargetId = targetId,
			Keys = keys,
			DelayMs = DelayMs,
			EnsureForeground = EnsureForeground,
		});
		if (response.Success != true)
			throw new AppDriverException(response.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, response.Error ?? "Keyboard command failed.");
	}

	private static bool IsModifierKey(WpfKey key) =>
		key == WpfKey.LeftCtrl
		|| key == WpfKey.RightCtrl
		|| key == WpfKey.LeftAlt
		|| key == WpfKey.RightAlt
		|| key == WpfKey.LeftShift
		|| key == WpfKey.RightShift
		|| key == WpfKey.LWin
		|| key == WpfKey.RWin;

	private static void SendVirtualKey(WpfKey key, bool isKeyDown)
	{
		var code = WpfKeyInterop.VirtualKeyFromKey(key);
		if (code == -1)
			throw new ArgumentException($"Invalid key: {key}", nameof(key));

		SendKeyInput((ushort)(code & 0xffff), isKeyDown);
	}

	private static List<ushort> PressShiftState(byte shiftState)
	{
		var modifiers = new List<ushort>(capacity: 3);
		if ((shiftState & 1) != 0)
			PressModifier(NativeMethods.VK_SHIFT, modifiers);
		if ((shiftState & 2) != 0)
			PressModifier(NativeMethods.VK_CONTROL, modifiers);
		if ((shiftState & 4) != 0)
			PressModifier(NativeMethods.VK_MENU, modifiers);
		return modifiers;
	}

	private static void PressModifier(ushort modifier, List<ushort> modifiers)
	{
		SendKeyInput(modifier, isKeyDown: true);
		modifiers.Add(modifier);
	}

	private static void ReleaseShiftState(List<ushort> modifiers)
	{
		for (var i = modifiers.Count - 1; i >= 0; i--)
			SendKeyInput(modifiers[i], isKeyDown: false);
	}

	private static void SendUnicode(char character, bool isKeyDown) =>
		SendKeyboardInput(new KeyboardInput
		{
			Scan = character,
			Flags = NativeMethods.KEYEVENTF_UNICODE | (isKeyDown ? 0u : NativeMethods.KEYEVENTF_KEYUP),
			ExtraInfo = NativeMethods.GetMessageExtraInfo(),
		});

	private static void SendKeyInput(ushort keyCode, bool isKeyDown) =>
		SendKeyboardInput(new KeyboardInput
		{
			VirtualKey = keyCode,
			Flags = isKeyDown ? 0u : NativeMethods.KEYEVENTF_KEYUP,
			ExtraInfo = NativeMethods.GetMessageExtraInfo(),
		});

	private static void SendKeyboardInput(KeyboardInput keyboardInput)
	{
		var input = Input.Keyboard(keyboardInput);
		NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Input
	{
		public int Type;
		public InputUnion Union;

		public static Input Keyboard(KeyboardInput input) =>
			new()
			{
				Type = NativeMethods.INPUT_KEYBOARD,
				Union = new InputUnion { Keyboard = input },
			};
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct InputUnion
	{
		[FieldOffset(0)]
		public KeyboardInput Keyboard;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KeyboardInput
	{
		public ushort VirtualKey;
		public ushort Scan;
		public uint Flags;
		public uint Time;
		public IntPtr ExtraInfo;
	}

	private static class NativeMethods
	{
		public const int INPUT_KEYBOARD = 1;
		public const ushort VK_SHIFT = 0x10;
		public const ushort VK_CONTROL = 0x11;
		public const ushort VK_MENU = 0x12;
		public const uint KEYEVENTF_KEYUP = 0x0002;
		public const uint KEYEVENTF_UNICODE = 0x0004;

		[DllImport("user32.dll", SetLastError = true)]
		public static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

		[DllImport("user32.dll")]
		public static extern IntPtr GetMessageExtraInfo();

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern short VkKeyScan(char character);
	}
}
