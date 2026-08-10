namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Shared;
using WpfKey = System.Windows.Input.Key;
using WpfKeyInterop = System.Windows.Input.KeyInterop;

public sealed class Keyboard
{
	private readonly AppDriver driver;
	private TimeSpan delay = TimeSpan.FromMilliseconds(TimeoutDefaults.KeyboardDelayMs);

	public Keyboard(AppDriver driver)
	{
		this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
	}

	public TimeSpan Delay
	{
		get => delay;
		set
		{
			_ = DurationUtility.ToMilliseconds(value, nameof(value), allowZero: true);
			delay = value;
		}
	}

	public bool EnsureForeground { get; set; } = true;

	public void Press(params WpfKey[] keys)
	{
		if (keys is null || keys.Length == 0)
			throw new ArgumentException("At least one key is required.", nameof(keys));

		List<WpfKey> heldModifiers = [];
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
				var delayMs = DelayMilliseconds;
				if (delayMs > 0)
					Thread.Sleep(delayMs);
			}
		}
		finally
		{
			for (var i = heldModifiers.Count - 1; i >= 0; i--)
				SendVirtualKey(heldModifiers[i], isKeyDown: false);
		}

		driver.RefreshAfterPhysicalInput();
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

			var delayMs = DelayMilliseconds;
			if (delayMs > 0)
				Thread.Sleep(Math.Min(delayMs, TimeoutDefaults.KeyboardPhysicalDelayCapMs));
		}

		driver.RefreshAfterPhysicalInput();
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
		var response = driver.SendCommand<StandardIpcResponse>(new KeyPressCommandRequest
		{
			TargetId = targetId,
			Keys = keys,
			DelayMs = DelayMilliseconds,
			EnsureForeground = EnsureForeground,
		});
		if (response.Success != true)
			throw new AppDriverException(response.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, response.Error ?? "Keyboard command failed.");
	}

	private int DelayMilliseconds => DurationUtility.ToMilliseconds(Delay, nameof(Delay), allowZero: true);

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
		SendKeyboardInput(new NativeMethods.KeyboardInputData
		{
			Scan = character,
			Flags = NativeMethods.KEYEVENTF_UNICODE | (isKeyDown ? 0u : NativeMethods.KEYEVENTF_KEYUP),
			ExtraInfo = NativeMethods.GetMessageExtraInfo(),
		});

	private static void SendKeyInput(ushort keyCode, bool isKeyDown) =>
		SendKeyboardInput(new NativeMethods.KeyboardInputData
		{
			VirtualKey = keyCode,
			Flags = isKeyDown ? 0u : NativeMethods.KEYEVENTF_KEYUP,
			ExtraInfo = NativeMethods.GetMessageExtraInfo(),
		});

	private static void SendKeyboardInput(NativeMethods.KeyboardInputData keyboardInput)
	{
		var input = NativeMethods.Input.Keyboard(keyboardInput);
		NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeMethods.Input)));
	}
}
