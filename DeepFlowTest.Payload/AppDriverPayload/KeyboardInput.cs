namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DeepFlowTest.Contracts;
using DeepFlowTest.Shared;
using Forms = System.Windows.Forms;

internal static class KeyboardInput
{
	public static void TypeTextComposition(string text)
	{
		TypeTextComposition(InputManager.Current.PrimaryKeyboardDevice.FocusedElement, text);
	}

	public static void TypeTextComposition(IInputElement? target, string text)
	{
		var keyboard = InputManager.Current.PrimaryKeyboardDevice;
		var focusedElement = target ?? keyboard.FocusedElement;
		var args = new TextCompositionEventArgs(
			keyboard,
			new TextComposition(InputManager.Current, focusedElement, text ?? string.Empty))
		{
			RoutedEvent = UIElement.PreviewTextInputEvent,
			Source = focusedElement,
		};
		InputManager.Current.ProcessInput(args);
	}

	public static void TypePhysical(string text, int delayMs = TimeoutDefaults.KeyboardTextInputDelayMs)
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
				Thread.Sleep(Math.Min(delayMs, TimeoutDefaults.KeyboardPhysicalDelayCapMs));
		}
	}

	public static void PressPhysical(IEnumerable<Key> keys, int delayMs = TimeoutDefaults.KeyboardDelayMs)
	{
		List<Key> heldModifiers = [];
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

	public static bool TryPressWpf(IInputElement target, IReadOnlyList<Key[]> keyGroups, int delayMs, out string? error)
	{
		error = null;
		if (target is null)
		{
			error = "WPF key input requires an input element target.";
			return false;
		}

		if (keyGroups.Count == 0)
		{
			error = "Key input cannot be empty.";
			return false;
		}

		var inputSource = GetInputSource(target);
		if (inputSource is null)
		{
			error = "WPF key input requires a target with an active presentation source.";
			return false;
		}

		using var syntheticKeyboard = AppHooks.BeginSyntheticKeyboardInput();
		foreach (var group in keyGroups)
			PressWpfGroup(target, inputSource, group, delayMs);

		return true;
	}

	public static bool TryPressNativeHwnd(IntPtr hwnd, IReadOnlyList<Key[]> keyGroups, int delayMs)
	{
		if (hwnd == IntPtr.Zero || keyGroups.Count == 0)
			return false;

		var success = true;
		foreach (var group in keyGroups)
		{
			using var nativeKeyboardState = BeginNativeKeyboardState(group);
			success &= PressNativeHwndGroup(hwnd, group, delayMs);
			Forms.Application.DoEvents();
		}

		return success;
	}

	public static IReadOnlyList<Key[]> ParseKeyGroups(object? rawKeys)
	{
		if (rawKeys is null)
			return [];

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

	private static void PressWpfGroup(IInputElement target, PresentationSource inputSource, IReadOnlyList<Key> keys, int delayMs)
	{
		List<Key> heldModifiers = [];
		try
		{
			foreach (var key in keys)
			{
				if (IsModifierKey(key))
				{
					if (!heldModifiers.Contains(key))
					{
						heldModifiers.Add(key);
						AppHooks.SetSyntheticKeyboardState(heldModifiers);
						ProcessWpfKey(target, inputSource, key, isKeyDown: true);
					}

					continue;
				}

				var pressed = new List<Key>(heldModifiers.Count + 1);
				pressed.AddRange(heldModifiers);
				pressed.Add(key);
				AppHooks.SetSyntheticKeyboardState(pressed);
				var handled = ProcessWpfKey(target, inputSource, key, isKeyDown: true);
				if (!handled && TryGetTextComposition(key, heldModifiers, out var text))
					TypeTextComposition(target, text);

				AppHooks.SetSyntheticKeyboardState(heldModifiers);
				ProcessWpfKey(target, inputSource, key, isKeyDown: false);
				if (delayMs > 0)
					Thread.Sleep(delayMs);
			}
		}
		finally
		{
			for (var i = heldModifiers.Count - 1; i >= 0; i--)
			{
				var key = heldModifiers[i];
				heldModifiers.RemoveAt(i);
				AppHooks.SetSyntheticKeyboardState(heldModifiers);
				ProcessWpfKey(target, inputSource, key, isKeyDown: false);
			}
		}
	}

	private static bool ProcessWpfKey(IInputElement target, PresentationSource inputSource, Key key, bool isKeyDown)
	{
		var previewEvent = isKeyDown ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent;
		return ProcessWpfKeyEvent(target, inputSource, key, previewEvent);
	}

	private static bool ProcessWpfKeyEvent(IInputElement target, PresentationSource inputSource, Key key, RoutedEvent routedEvent)
	{
		var args = new KeyEventArgs(InputManager.Current.PrimaryKeyboardDevice, inputSource, Environment.TickCount, key)
		{
			RoutedEvent = routedEvent,
			Source = target,
		};
		InputManager.Current.ProcessInput(args);
		return args.Handled;
	}

	private static PresentationSource? GetInputSource(IInputElement target)
	{
		if (target is DependencyObject dependencyObject)
			return PresentationSource.FromDependencyObject(dependencyObject);

		if (InputManager.Current.PrimaryKeyboardDevice.FocusedElement is DependencyObject focusedDependencyObject)
			return PresentationSource.FromDependencyObject(focusedDependencyObject);

		return null;
	}

	private static bool PressNativeHwndGroup(IntPtr hwnd, IReadOnlyList<Key> keys, int delayMs)
	{
		List<Key> heldModifiers = [];
		var hasAlt = keys.Any(IsAltKey);
		var success = true;
		try
		{
			foreach (var key in keys)
			{
				if (IsModifierKey(key))
				{
					if (!heldModifiers.Contains(key))
					{
						success &= PostNativeKey(hwnd, key, isKeyDown: true, isSystemKey: hasAlt);
						heldModifiers.Add(key);
					}

					continue;
				}

				success &= PostNativeKey(hwnd, key, isKeyDown: true, isSystemKey: hasAlt);
				if (TryGetTextComposition(key, heldModifiers, out var text))
					success &= PostNativeText(hwnd, text);
				success &= PostNativeKey(hwnd, key, isKeyDown: false, isSystemKey: hasAlt);
				if (delayMs > 0)
					Thread.Sleep(delayMs);
			}
		}
		finally
		{
			for (var i = heldModifiers.Count - 1; i >= 0; i--)
				success &= PostNativeKey(hwnd, heldModifiers[i], isKeyDown: false, isSystemKey: hasAlt);
		}

		return success;
	}

	private static IDisposable? BeginNativeKeyboardState(IReadOnlyList<Key> keys)
	{
		if (!keys.Any(IsModifierKey))
			return null;

		var original = new byte[256];
		if (!NativeMethods.GetKeyboardState(original))
			return null;

		var synthetic = (byte[])original.Clone();
		foreach (var key in keys)
			SetNativeModifierState(synthetic, key);

		return NativeMethods.SetKeyboardState(synthetic)
			? new NativeKeyboardStateScope(original)
			: null;
	}

	private static void SetNativeModifierState(byte[] keyboardState, Key key)
	{
		switch (key)
		{
			case Key.LeftCtrl:
				SetNativeKeyDown(keyboardState, NativeMethods.VK_CONTROL);
				SetNativeKeyDown(keyboardState, NativeMethods.VK_LCONTROL);
				break;
			case Key.RightCtrl:
				SetNativeKeyDown(keyboardState, NativeMethods.VK_CONTROL);
				SetNativeKeyDown(keyboardState, NativeMethods.VK_RCONTROL);
				break;
			case Key.LeftShift:
				SetNativeKeyDown(keyboardState, NativeMethods.VK_SHIFT);
				SetNativeKeyDown(keyboardState, NativeMethods.VK_LSHIFT);
				break;
			case Key.RightShift:
				SetNativeKeyDown(keyboardState, NativeMethods.VK_SHIFT);
				SetNativeKeyDown(keyboardState, NativeMethods.VK_RSHIFT);
				break;
			case Key.LeftAlt:
				SetNativeKeyDown(keyboardState, NativeMethods.VK_MENU);
				SetNativeKeyDown(keyboardState, NativeMethods.VK_LMENU);
				break;
			case Key.RightAlt:
				SetNativeKeyDown(keyboardState, NativeMethods.VK_MENU);
				SetNativeKeyDown(keyboardState, NativeMethods.VK_RMENU);
				break;
		}
	}

	private static void SetNativeKeyDown(byte[] keyboardState, ushort virtualKey)
	{
		if (virtualKey < keyboardState.Length)
			keyboardState[virtualKey] = 0x80;
	}

	private static bool PostNativeKey(IntPtr hwnd, Key key, bool isKeyDown, bool isSystemKey)
	{
		var virtualKey = KeyInterop.VirtualKeyFromKey(key);
		if (virtualKey == -1)
			return false;

		var message = isSystemKey
			? isKeyDown ? NativeMethods.WM_SYSKEYDOWN : NativeMethods.WM_SYSKEYUP
			: isKeyDown ? NativeMethods.WM_KEYDOWN : NativeMethods.WM_KEYUP;
		return NativeMethods.PostMessage(
			hwnd,
			message,
			new IntPtr(virtualKey),
			CreateNativeKeyLParam(virtualKey, isKeyDown, isSystemKey));
	}

	private static bool PostNativeText(IntPtr hwnd, string text)
	{
		var success = true;
		foreach (var character in text)
			success &= NativeMethods.PostMessage(hwnd, NativeMethods.WM_CHAR, new IntPtr(character), IntPtr.Zero);
		return success;
	}

	private static IntPtr CreateNativeKeyLParam(int virtualKey, bool isKeyDown, bool isSystemKey)
	{
		var scanCode = (int)(NativeMethods.MapVirtualKey((uint)virtualKey, NativeMethods.MAPVK_VK_TO_VSC) & 0xff);
		var value = 1 | (scanCode << 16);
		if (IsExtendedVirtualKey(virtualKey))
			value |= 1 << 24;
		if (isSystemKey)
			value |= 1 << 29;
		if (!isKeyDown)
			value |= unchecked((int)0xC0000000);
		return new IntPtr(value);
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
			"backspace" => nameof(Key.Back),
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

	private static bool IsAltKey(Key key) =>
		key == Key.LeftAlt || key == Key.RightAlt;

	private static bool TryGetTextComposition(Key key, IReadOnlyCollection<Key> heldModifiers, out string text)
	{
		text = string.Empty;
		if (heldModifiers.Any(static modifier => modifier is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin))
			return false;

		var shifted = heldModifiers.Any(static modifier => modifier is Key.LeftShift or Key.RightShift);
		if (key is >= Key.A and <= Key.Z)
		{
			var character = (char)('a' + ((int)key - (int)Key.A));
			text = shifted ? char.ToUpperInvariant(character).ToString() : character.ToString();
			return true;
		}

		if (key is >= Key.D0 and <= Key.D9)
		{
			var digit = (char)('0' + ((int)key - (int)Key.D0));
			text = shifted ? GetShiftedDigit(digit).ToString() : digit.ToString();
			return true;
		}

		if (key is >= Key.NumPad0 and <= Key.NumPad9)
		{
			text = ((char)('0' + ((int)key - (int)Key.NumPad0))).ToString();
			return true;
		}

		if (key == Key.Space)
		{
			text = " ";
			return true;
		}

		return TryGetPunctuationText(key, shifted, out text);
	}

	private static char GetShiftedDigit(char digit) =>
		digit switch
		{
			'1' => '!',
			'2' => '@',
			'3' => '#',
			'4' => '$',
			'5' => '%',
			'6' => '^',
			'7' => '&',
			'8' => '*',
			'9' => '(',
			'0' => ')',
			_ => digit,
		};

	private static bool TryGetPunctuationText(Key key, bool shifted, out string text)
	{
		text = key switch
		{
			Key.OemMinus => shifted ? "_" : "-",
			Key.OemPlus => shifted ? "+" : "=",
			Key.OemOpenBrackets => shifted ? "{" : "[",
			Key.OemCloseBrackets => shifted ? "}" : "]",
			Key.OemPipe => shifted ? "|" : "\\",
			Key.OemSemicolon => shifted ? ":" : ";",
			Key.OemQuotes => shifted ? "\"" : "'",
			Key.OemComma => shifted ? "<" : ",",
			Key.OemPeriod => shifted ? ">" : ".",
			Key.OemQuestion => shifted ? "?" : "/",
			Key.OemTilde => shifted ? "~" : "`",
			_ => string.Empty,
		};
		return text.Length != 0;
	}

	private static bool IsExtendedVirtualKey(int virtualKey) =>
		virtualKey is 0x21
			or 0x22
			or 0x23
			or 0x24
			or 0x25
			or 0x26
			or 0x27
			or 0x28
			or 0x2D
			or 0x2E
			or 0x6F
			or 0x90
			or 0xA3
			or 0xA5;

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

	private sealed class NativeKeyboardStateScope : IDisposable
	{
		private readonly byte[] original;
		private int disposed;

		public NativeKeyboardStateScope(byte[] original)
		{
			this.original = original;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
				NativeMethods.SetKeyboardState(original);
		}
	}
}
