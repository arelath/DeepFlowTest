namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using DeepFlowTest.AppDriverPayload.Native;

internal static class KeyboardInput
{
	public static void TypeTextComposition(string text)
	{
		var keyboard = InputManager.Current.PrimaryKeyboardDevice;
		var focusedElement = keyboard.FocusedElement;
		var args = new TextCompositionEventArgs(
			keyboard,
			new TextComposition(InputManager.Current, focusedElement, text ?? string.Empty))
		{
			RoutedEvent = UIElement.PreviewTextInputEvent,
			Source = focusedElement,
		};
		InputManager.Current.ProcessInput(args);
	}

	public static void TypePhysical(string text, int delayMs = 20)
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
				ReleaseModifiers(modifiers);
			}

			if (delayMs > 0)
				Thread.Sleep(Math.Min(delayMs, 50));
		}
	}

	public static void PressPhysical(IEnumerable<Key> keys, int delayMs = 50)
	{
		var heldModifiers = new List<Key>();
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
				if (delayMs > 0)
					Thread.Sleep(delayMs);
			}
		}
		finally
		{
			for (var i = heldModifiers.Count - 1; i >= 0; i--)
				SendVirtualKey(heldModifiers[i], isKeyDown: false);
		}
	}

	public static IReadOnlyList<Key[]> ParseKeyGroups(object? rawKeys)
	{
		if (rawKeys is null)
			return Array.Empty<Key[]>();

		if (rawKeys is string stringKeys)
			return stringKeys
				.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(ParseKeyGroup)
				.Where(static group => group.Length != 0)
				.ToArray();

		if (rawKeys is IEnumerable enumerable)
			return enumerable
				.Cast<object?>()
				.Select(item => ParseKeyGroup(Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty))
				.Where(static group => group.Length != 0)
				.ToArray();

		return new[] { ParseKeyGroup(Convert.ToString(rawKeys, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty) };
	}

	private static Key[] ParseKeyGroup(string value)
	{
		return value
			.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(static token => token.Trim())
			.Where(static token => token.Length != 0)
			.Select(ParseKey)
			.ToArray();
	}

	private static Key ParseKey(string value)
	{
		var normalized = NormalizeKeyName(value);
		return (Key)Enum.Parse(typeof(Key), normalized, ignoreCase: true);
	}

	private static string NormalizeKeyName(string value)
	{
		var trimmed = value.Trim();
		if (trimmed.Length == 1 && char.IsDigit(trimmed[0]))
			return "D" + trimmed;

		return trimmed.ToLowerInvariant() switch
		{
			"ctrl" or "control" => nameof(Key.LeftCtrl),
			"alt" => nameof(Key.LeftAlt),
			"shift" => nameof(Key.LeftShift),
			"esc" => nameof(Key.Escape),
			"del" => nameof(Key.Delete),
			"ins" => nameof(Key.Insert),
			"enter" => nameof(Key.Return),
			_ => trimmed,
		};
	}

	private static bool IsModifierKey(Key key) =>
		key == Key.LeftCtrl
		|| key == Key.RightCtrl
		|| key == Key.LeftAlt
		|| key == Key.RightAlt
		|| key == Key.LeftShift
		|| key == Key.RightShift
		|| key == Key.LWin
		|| key == Key.RWin;

	private static void SendVirtualKey(Key key, bool isKeyDown)
	{
		var code = KeyInterop.VirtualKeyFromKey(key);
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

	private static void ReleaseModifiers(List<ushort> modifiers)
	{
		for (var i = modifiers.Count - 1; i >= 0; i--)
			SendKeyInput(modifiers[i], isKeyDown: false);
	}

	private static void SendUnicode(char character, bool isKeyDown) =>
		SendKeyboardInput(new KeyboardInputData
		{
			Scan = character,
			Flags = NativeMethods.KEYEVENTF_UNICODE | (isKeyDown ? 0u : NativeMethods.KEYEVENTF_KEYUP),
			ExtraInfo = NativeMethods.GetMessageExtraInfo(),
		});

	private static void SendKeyInput(ushort keyCode, bool isKeyDown) =>
		SendKeyboardInput(new KeyboardInputData
		{
			VirtualKey = keyCode,
			Flags = isKeyDown ? 0u : NativeMethods.KEYEVENTF_KEYUP,
			ExtraInfo = NativeMethods.GetMessageExtraInfo(),
		});

	private static void SendKeyboardInput(KeyboardInputData keyboardInput)
	{
		var input = Input.Keyboard(keyboardInput);
		NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
	}
}
