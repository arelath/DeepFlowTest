namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Controls.Ribbon;
using System.Windows.Input;
using System.Windows.Media;
using DeepFlowTest.AppDriverPayload.Patching;
using HarmonyLib;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

public static class AppHooks
{
	private static readonly object Gate = new();
	private static WpfPatchResult lastResult = new() { FrameworkFamily = RuntimeFrameworkFamilies.Unknown };
	private static bool hooksApplied;
	private static bool dialogHooksApplied;
	private static volatile bool showDialogCalled;
	private static int syntheticMouseInputDepth;
	private static int syntheticKeyboardInputDepth;
	private static int syntheticInputDepth;
	private static Point? syntheticMouseScreenPosition;
	private static IInputElement? syntheticCapturedMouseElement;
	private static IInputElement? syntheticMouseHitTarget;
	private static object? syntheticDragDropData;
	private static DragDropEffects syntheticDragDropAllowedEffects;
	private static readonly HashSet<Key> SyntheticPressedKeys = [];
	private static ModifierKeys syntheticModifiers;

	public static WpfPatchResult LastResult
	{
		get
		{
			lock (Gate)
				return lastResult;
		}
	}

	public static WpfPatchResult Apply(Action<string, Exception?>? log = null, RuntimeWpfPatchCoordinator? coordinator = null)
	{
		// Install ONLY the dialog-detection hooks (MessageBox.Show / Window.ShowDialog /
		// CommonDialog.ShowDialog, which flag ShowDialogCalled so the dispatcher's modal-watch can
		// detect a blocking native dialog) unconditionally at startup. These must not be gated on the
		// optional cosmetic patches below: on .NET Framework every optional patch reports unavailable
		// and is skipped, so relying on their apply-action to call EnsureHooked left the dialog hooks
		// uninstalled there -- modal detection then fell back to slow native enumeration and stalled
		// shutdown. We deliberately do NOT call the full EnsureHooked() here: that also installs the
		// mouse/keyboard/menu input-interception patches, which on net-framework were never active
		// (EnsureHooked only ran via the optional patches, all skipped there) and turning them on
		// unconditionally changes input behavior the existing tests depend on. EnsureDialogHooked is
		// idempotent and independent of EnsureHooked's full-patch flag.
		try
		{
			EnsureDialogHooked();
			log?.Invoke("Dialog detection hooks installed.", null);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			log?.Invoke("Dialog detection hooks failed to install; continuing startup.", ex);
		}

		var result = (coordinator ?? RuntimeWpfPatchCoordinator.Default).ApplyCurrentRuntime(log);
		lock (Gate)
			lastResult = result;
		return result;
	}

	// The dialog patch classes whose Prefix sets ShowDialogCalled. Kept separate from the full
	// PatchAll set so they can be installed without the input-interception patches.
	private static readonly Type[] DialogHookPatchClasses =
	{
		typeof(PatchWindowShowDialog),
		typeof(PatchMessageBoxShow),
		typeof(PatchCommonDialogShowDialog),
	};

	// Installs just the dialog-detection hooks. Safe to call repeatedly and safe to call before or
	// after EnsureHooked: PatchAll skips members that are already patched, so the later call is a
	// no-op for the dialog members.
	public static void EnsureDialogHooked()
	{
		lock (Gate)
		{
			if (dialogHooksApplied || hooksApplied)
				return;

			var harmony = new Harmony("com.deepflowtest.apphooks.dialog");
			foreach (var patchClass in DialogHookPatchClasses)
				harmony.CreateClassProcessor(patchClass).Patch();

			dialogHooksApplied = true;
		}
	}

	public static void EnsureHooked()
	{
		lock (Gate)
		{
			if (hooksApplied)
				return;

			WarmupHookedMembers();
			var harmony = new Harmony("com.deepflowtest.apphooks.patch");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			hooksApplied = true;
		}
	}

	public static void SetButton(MouseButton mouseButton, bool isPressed)
	{
		if (mouseButton == MouseButton.Left)
			IsLeftMousePressed = isPressed;
		else if (mouseButton == MouseButton.Right)
			IsRightMousePressed = isPressed;
	}

	public static void ResetMouseState()
	{
		IsLeftMousePressed = null;
		IsRightMousePressed = null;
		syntheticMouseScreenPosition = null;
		syntheticCapturedMouseElement = null;
		syntheticMouseHitTarget = null;
		syntheticDragDropData = null;
		syntheticDragDropAllowedEffects = DragDropEffects.None;
	}

	public static void SetSyntheticMouseScreenPosition(Point? screenPosition) =>
		syntheticMouseScreenPosition = screenPosition;

	public static void SetSyntheticMouseHitTarget(IInputElement? target) =>
		syntheticMouseHitTarget = target;

	public static bool TryGetSyntheticDragDrop(out object data, out DragDropEffects allowedEffects)
	{
		if (syntheticDragDropData is null)
		{
			data = null!;
			allowedEffects = DragDropEffects.None;
			return false;
		}

		data = syntheticDragDropData;
		allowedEffects = syntheticDragDropAllowedEffects;
		return true;
	}

	public static bool IsSyntheticMouseInputActive => Volatile.Read(ref syntheticMouseInputDepth) > 0;

	public static bool IsSyntheticKeyboardInputActive => Volatile.Read(ref syntheticKeyboardInputDepth) > 0;

	public static bool IsSyntheticInputActive => Volatile.Read(ref syntheticInputDepth) > 0;

	public static IDisposable BeginSyntheticInput()
	{
		Interlocked.Increment(ref syntheticInputDepth);
		return new SyntheticInputScope(includeMouse: false, includeKeyboard: false);
	}

	public static IDisposable BeginSyntheticMouseInput()
	{
		Interlocked.Increment(ref syntheticInputDepth);
		Interlocked.Increment(ref syntheticMouseInputDepth);
		return new SyntheticInputScope(includeMouse: true, includeKeyboard: false);
	}

	public static IDisposable BeginSyntheticKeyboardInput()
	{
		Interlocked.Increment(ref syntheticInputDepth);
		Interlocked.Increment(ref syntheticKeyboardInputDepth);
		return new SyntheticInputScope(includeMouse: false, includeKeyboard: true);
	}

	public static void SetSyntheticKeyboardState(IEnumerable<Key> pressedKeys)
	{
		lock (Gate)
		{
			SyntheticPressedKeys.Clear();
			syntheticModifiers = ModifierKeys.None;
			foreach (var key in pressedKeys)
			{
				SyntheticPressedKeys.Add(key);
				syntheticModifiers |= ToModifierKeys(key);
			}
		}
	}

	public static void ResetKeyboardState()
	{
		lock (Gate)
		{
			SyntheticPressedKeys.Clear();
			syntheticModifiers = ModifierKeys.None;
		}
	}

	public static bool ShowDialogCalled
	{
		get => showDialogCalled;
		set => showDialogCalled = value;
	}

	public static bool? IsLeftMousePressed { get; private set; }

	public static bool? IsRightMousePressed { get; private set; }

	public static FieldInfo? MouseOverElement { get; } = typeof(MouseDevice).GetField("_mouseOver", BindingFlags.NonPublic | BindingFlags.Instance);

	public static MethodInfo? WriteElementOverElement { get; } = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.NonPublic | BindingFlags.Instance);

	[Flags]
	public enum CoreFlags : uint
	{
		IsMouseOverCache = 0x00001000,
	}

	public static void ResetForTests()
	{
		lock (Gate)
			lastResult = new WpfPatchResult { FrameworkFamily = RuntimeFrameworkFamilies.Unknown };
		ShowDialogCalled = false;
		ResetMouseState();
		ResetKeyboardState();
		Volatile.Write(ref syntheticMouseInputDepth, 0);
		Volatile.Write(ref syntheticKeyboardInputDepth, 0);
		Volatile.Write(ref syntheticInputDepth, 0);
	}

	private static void WarmupHookedMembers()
	{
		TryWarmup(() => _ = Mouse.PrimaryDevice.LeftButton);
		TryWarmup(() => _ = Keyboard.PrimaryDevice.Modifiers);
		TryWarmup(() => _ = Keyboard.PrimaryDevice.GetKeyStates(Key.LeftCtrl));
		TryWarmup(() => _ = Keyboard.PrimaryDevice.IsKeyDown(Key.LeftCtrl));
		TryWarmup(() => _ = Keyboard.GetKeyStates(Key.LeftCtrl));
		TryWarmup(() => _ = Keyboard.IsKeyDown(Key.LeftCtrl));
		TryWarmup(() => InvokeBestMatch(typeof(ButtonBase), new Button(), "UpdateIsPressed"));
		TryWarmup(() => InvokeBestMatch(typeof(GridViewColumnHeader), new GridViewColumnHeader(), "IsMouseOutside"));
		TryWarmup(() =>
		{
			var menuItem = new MenuItem();
			InvokeBestMatch(
				typeof(MenuItem),
				menuItem,
				"HandleMouseDown",
				new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = MenuItem.MouseDownEvent });
			InvokeBestMatch(
				typeof(MenuItem),
				menuItem,
				"HandleMouseUp",
				new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = MenuItem.MouseUpEvent });
			InvokeBestMatch(typeof(MenuItem), menuItem, "UpdateIsPressed");
		});
		TryWarmup(() => InvokeBestMatch(typeof(RibbonMenuItem), new RibbonMenuItem(), "UpdateIsPressed"));
		TryWarmup(() => InvokeBestMatch(
			typeof(DataGridCheckBoxColumn),
			null,
			"IsMouseOver",
			new CheckBox(),
			new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = MenuItem.MouseUpEvent }));
	}

	private static void TryWarmup(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
		}
	}

	private static object? InvokeBestMatch(Type type, object? target, string methodName, params object?[] args)
	{
		var methods = GetCandidateMethods(type, methodName, InvokeAllBindings, args);
		if (methods.Count == 0)
			return null;

		var method = methods[0];
		try
		{
			return method.Invoke(target, args);
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			throw ex.InnerException;
		}
	}

	private static IReadOnlyList<MethodInfo> GetCandidateMethods(Type type, string methodName, BindingFlags bindingFlags, object?[] args)
	{
		List<MethodInfo> methods = [];
		foreach (var method in type.GetMethods(bindingFlags))
		{
			if (method.Name != methodName || !ParametersMatch(method.GetParameters(), args))
				continue;

			methods.Add(method);
		}

		methods.Sort((left, right) =>
		{
			var accessComparison = GetMethodAccessRank(left) - GetMethodAccessRank(right);
			return accessComparison != 0 ? accessComparison : GetParameterMatchScore(left.GetParameters(), args).CompareTo(GetParameterMatchScore(right.GetParameters(), args));
		});
		return methods;
	}

	private static bool ParametersMatch(ParameterInfo[] parameterInfos, object?[] args)
	{
		if (parameterInfos.Length != args.Length)
			return false;

		for (var i = 0; i < parameterInfos.Length; i++)
		{
			var parameterType = parameterInfos[i].ParameterType.IsByRef
				? parameterInfos[i].ParameterType.GetElementType() ?? parameterInfos[i].ParameterType
				: parameterInfos[i].ParameterType;
			var arg = args[i];
			if (arg is null)
			{
				if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) is null)
					return false;
				continue;
			}

			if (!parameterType.IsAssignableFrom(arg.GetType()))
				return false;
		}

		return true;
	}

	private static int GetParameterMatchScore(ParameterInfo[] parameterInfos, object?[] args)
	{
		var score = 0;
		for (var i = 0; i < parameterInfos.Length; i++)
		{
			var arg = args[i];
			if (arg is null)
			{
				score++;
				continue;
			}

			var parameterType = parameterInfos[i].ParameterType.IsByRef
				? parameterInfos[i].ParameterType.GetElementType() ?? parameterInfos[i].ParameterType
				: parameterInfos[i].ParameterType;
			if (parameterType != arg.GetType())
				score++;
		}

		return score;
	}

	private static int GetMethodAccessRank(MethodInfo method) =>
		method.IsPublic ? 0 : method.IsAssembly ? 1 : 2;

	private static ModifierKeys GetSyntheticModifiers()
	{
		lock (Gate)
			return syntheticModifiers;
	}

	private static bool IsSyntheticKeyDown(Key key)
	{
		lock (Gate)
			return SyntheticPressedKeys.Contains(key)
				|| key == Key.LeftCtrl && SyntheticPressedKeys.Contains(Key.RightCtrl)
				|| key == Key.RightCtrl && SyntheticPressedKeys.Contains(Key.LeftCtrl)
				|| key == Key.LeftShift && SyntheticPressedKeys.Contains(Key.RightShift)
				|| key == Key.RightShift && SyntheticPressedKeys.Contains(Key.LeftShift)
				|| key == Key.LeftAlt && SyntheticPressedKeys.Contains(Key.RightAlt)
				|| key == Key.RightAlt && SyntheticPressedKeys.Contains(Key.LeftAlt)
				|| key == Key.LWin && SyntheticPressedKeys.Contains(Key.RWin)
				|| key == Key.RWin && SyntheticPressedKeys.Contains(Key.LWin);
	}

	private static ModifierKeys ToModifierKeys(Key key) =>
		key switch
		{
			Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
			Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
			Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
			Key.LWin or Key.RWin => ModifierKeys.Windows,
			_ => ModifierKeys.None,
		};

	[HarmonyPatch(typeof(MouseDevice), "GetButtonState")]
	public static class PatchMouseDeviceGetButtonState
	{
		public static bool Prefix(ref MouseButtonState __result, MouseButton mouseButton)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			if (mouseButton == MouseButton.Right && IsRightMousePressed is not null)
			{
				__result = IsRightMousePressed == true ? MouseButtonState.Pressed : MouseButtonState.Released;
				return false;
			}

			if (mouseButton == MouseButton.Left && IsLeftMousePressed is not null)
			{
				__result = IsLeftMousePressed == true ? MouseButtonState.Pressed : MouseButtonState.Released;
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(DragDrop), nameof(DragDrop.DoDragDrop), new[] { typeof(DependencyObject), typeof(object), typeof(DragDropEffects) })]
	public static class PatchDragDropDoDragDrop
	{
		public static bool Prefix(DependencyObject dragSource, object data, DragDropEffects allowedEffects, ref DragDropEffects __result)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			_ = dragSource;
			syntheticDragDropData = data;
			syntheticDragDropAllowedEffects = allowedEffects;
			__result = allowedEffects;
			return false;
		}
	}

	[HarmonyPatch(typeof(KeyboardDevice), "get_Modifiers")]
	public static class PatchKeyboardDeviceModifiers
	{
		public static bool Prefix(ref ModifierKeys __result)
		{
			if (!IsSyntheticKeyboardInputActive)
				return true;

			__result = GetSyntheticModifiers();
			return false;
		}
	}

	[HarmonyPatch(typeof(KeyboardDevice), nameof(KeyboardDevice.GetKeyStates), new[] { typeof(Key) })]
	public static class PatchKeyboardDeviceGetKeyStates
	{
		public static bool Prefix(Key key, ref KeyStates __result)
		{
			if (!IsSyntheticKeyboardInputActive || !IsSyntheticKeyDown(key))
				return true;

			__result = KeyStates.Down;
			return false;
		}
	}

	[HarmonyPatch(typeof(KeyboardDevice), nameof(KeyboardDevice.IsKeyDown), new[] { typeof(Key) })]
	public static class PatchKeyboardDeviceIsKeyDown
	{
		public static bool Prefix(Key key, ref bool __result)
		{
			if (!IsSyntheticKeyboardInputActive || !IsSyntheticKeyDown(key))
				return true;

			__result = true;
			return false;
		}
	}

	[HarmonyPatch(typeof(Keyboard), nameof(Keyboard.GetKeyStates), new[] { typeof(Key) })]
	public static class PatchKeyboardGetKeyStates
	{
		public static bool Prefix(Key key, ref KeyStates __result)
		{
			if (!IsSyntheticKeyboardInputActive || !IsSyntheticKeyDown(key))
				return true;

			__result = KeyStates.Down;
			return false;
		}
	}

	[HarmonyPatch(typeof(Keyboard), nameof(Keyboard.IsKeyDown), new[] { typeof(Key) })]
	public static class PatchKeyboardIsKeyDown
	{
		public static bool Prefix(Key key, ref bool __result)
		{
			if (!IsSyntheticKeyboardInputActive || !IsSyntheticKeyDown(key))
				return true;

			__result = true;
			return false;
		}
	}

	[HarmonyPatch(typeof(MouseDevice), nameof(MouseDevice.GetPosition), new[] { typeof(IInputElement) })]
	public static class PatchMouseDeviceGetPosition
	{
		public static bool Prefix(IInputElement relativeTo, ref Point __result)
		{
			if (TryGetSyntheticMousePosition(relativeTo, out var position))
			{
				__result = position;
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(MouseEventArgs), nameof(MouseEventArgs.GetPosition), new[] { typeof(IInputElement) })]
	public static class PatchMouseEventArgsGetPosition
	{
		public static bool Prefix(IInputElement relativeTo, ref Point __result)
		{
			if (TryGetSyntheticMousePosition(relativeTo, out var position))
			{
				__result = position;
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(Mouse), nameof(Mouse.GetPosition), new[] { typeof(IInputElement) })]
	public static class PatchMouseGetPosition
	{
		public static bool Prefix(IInputElement relativeTo, ref Point __result)
		{
			if (TryGetSyntheticMousePosition(relativeTo, out var position))
			{
				__result = position;
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(UIElement), nameof(UIElement.CaptureMouse), new Type[] { })]
	public static class PatchUIElementCaptureMouse
	{
		public static bool Prefix(UIElement __instance, ref bool __result)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			syntheticCapturedMouseElement = __instance;
			__result = true;
			return false;
		}
	}

	[HarmonyPatch(typeof(UIElement), nameof(UIElement.ReleaseMouseCapture), new Type[] { })]
	public static class PatchUIElementReleaseMouseCapture
	{
		public static bool Prefix(UIElement __instance)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			if (ReferenceEquals(syntheticCapturedMouseElement, __instance))
				syntheticCapturedMouseElement = null;
			return false;
		}
	}

	[HarmonyPatch(typeof(Mouse), nameof(Mouse.Capture), new[] { typeof(IInputElement) })]
	public static class PatchMouseCapture
	{
		public static bool Prefix(IInputElement element, ref bool __result)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			syntheticCapturedMouseElement = element;
			__result = true;
			return false;
		}
	}

	[HarmonyPatch(typeof(Mouse), nameof(Mouse.Capture), new[] { typeof(IInputElement), typeof(CaptureMode) })]
	public static class PatchMouseCaptureWithMode
	{
		public static bool Prefix(IInputElement element, ref bool __result)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			syntheticCapturedMouseElement = element;
			__result = true;
			return false;
		}
	}

	[HarmonyPatch(typeof(Mouse), "get_Captured")]
	public static class PatchMouseCaptured
	{
		public static bool Prefix(ref IInputElement? __result)
		{
			if (!IsSyntheticMouseInputActive || syntheticCapturedMouseElement is null)
				return true;

			__result = syntheticCapturedMouseElement;
			return false;
		}
	}

	[HarmonyPatch(typeof(UIElement), nameof(UIElement.InputHitTest), new[] { typeof(Point) })]
	public static class PatchUIElementInputHitTest
	{
		public static bool Prefix(UIElement __instance, ref IInputElement? __result)
		{
			if (!IsSyntheticMouseInputActive || syntheticMouseHitTarget is not { } target)
				return true;
			if (!ContainsSyntheticHitTarget(__instance, target))
				return true;

			__result = target;
			return false;
		}
	}

	private static bool TryGetSyntheticMousePosition(IInputElement relativeTo, out Point position)
	{
		position = default;
		if (!IsSyntheticMouseInputActive || syntheticMouseScreenPosition is not { } screenPosition)
			return false;

		if (relativeTo is null)
		{
			position = screenPosition;
			return true;
		}

		if (relativeTo is not Visual visual)
			return false;

		try
		{
			position = visual.PointFromScreen(screenPosition);
		}
		catch (InvalidOperationException)
		{
			// Synthetic drag/drop can raise mouse moves while application handlers still reference
			// visuals that just detached. Keep the patched GetPosition path from falling through to
			// WPF's PointFromScreen exception; there is no meaningful coordinate for a detached
			// visual, and a default point is safer than failing the injected gesture.
			position = default;
		}

		return true;
	}

	private static bool ContainsSyntheticHitTarget(UIElement ancestor, IInputElement target)
	{
		if (ReferenceEquals(ancestor, target))
			return true;
		if (target is not DependencyObject current)
			return false;

		while (current is not null)
		{
			if (ReferenceEquals(current, ancestor))
				return true;

			var parent = default(DependencyObject);
			try
			{
				parent = VisualTreeHelper.GetParent(current);
			}
			catch (InvalidOperationException)
			{
			}

			current = parent ?? LogicalTreeHelper.GetParent(current);
		}

		return false;
	}

	[HarmonyPatch(typeof(ButtonBase), "UpdateIsPressed")]
	public static class PatchButtonUpdateIsPressed
	{
		private static readonly PropertyInfo? IsPressedProperty = typeof(ButtonBase).GetProperty("IsPressed", InvokeAllBindings);
		private static readonly MethodInfo? SetIsPressedMethod = GetBestMatchingMethod(typeof(ButtonBase), "SetIsPressed", false);

		public static bool Prefix(ButtonBase __instance)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			var isPressed = (bool)(IsPressedProperty?.GetValue(__instance) ?? false);
			SetIsPressedMethod?.Invoke(__instance, new object[] { !isPressed });
			return false;
		}
	}

	[HarmonyPatch(typeof(GridViewColumnHeader), "IsMouseOutside")]
	public static class PatchGridViewColumnHeaderIsMouseOutside
	{
		public static bool Prefix(ref bool __result)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(MenuItem), "HandleMouseDown")]
	public static class PatchMenuItemHandleMouseDown
	{
		private static readonly PropertyInfo? RoleProperty = typeof(MenuItem).GetProperty("Role", InvokeAllBindings);
		private static readonly MethodInfo? ClickHeaderMethod = GetBestMatchingMethod(typeof(MenuItem), "ClickHeader");

		public static bool Prefix(MenuItem __instance, object[] __args)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			if (__args.Length > 0 && __args[0] is MouseButtonEventArgs args)
			{
				var role = (MenuItemRole)(RoleProperty?.GetValue(__instance) ?? MenuItemRole.TopLevelItem);
				if (role is MenuItemRole.TopLevelHeader or MenuItemRole.SubmenuHeader)
					ClickHeaderMethod?.Invoke(__instance, []);

				args.Handled = true;
			}

			return false;
		}
	}

	[HarmonyPatch(typeof(MenuItem), "HandleMouseUp")]
	public static class PatchMenuItemHandleMouseUp
	{
		private static readonly PropertyInfo? RoleProperty = typeof(MenuItem).GetProperty("Role", InvokeAllBindings);
		private static readonly MethodInfo? ClickItemMethod = GetBestMatchingMethod(typeof(MenuItem), "ClickItem", true);

		public static bool Prefix(MenuItem __instance, object[] __args)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			if (__args.Length > 0 && __args[0] is MouseButtonEventArgs args)
			{
				var role = (MenuItemRole)(RoleProperty?.GetValue(__instance) ?? MenuItemRole.TopLevelItem);
				if (role is MenuItemRole.TopLevelItem or MenuItemRole.SubmenuItem)
					ClickItemMethod?.Invoke(__instance, new object[] { true });

				if (args.ChangedButton != MouseButton.Right)
					args.Handled = true;
			}

			return false;
		}
	}

	[HarmonyPatch(typeof(MenuItem), "UpdateIsPressed")]
	public static class PatchMenuItemUpdateIsPressed
	{
		private static readonly PropertyInfo? IsPressedProperty = typeof(MenuItem).GetProperty("IsPressed", InvokeAllBindings);
		private static readonly FieldInfo? IsPressedPropertyKeyField = typeof(MenuItem).GetField("IsPressedPropertyKey", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
		private static readonly MethodInfo? ClearValueMethod = typeof(MenuItem).GetMethod("ClearValue", InvokeAllBindings, null, new[] { typeof(DependencyPropertyKey) }, null);

		public static bool Prefix(MenuItem __instance)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			if (Mouse.LeftButton == MouseButtonState.Pressed)
			{
				IsPressedProperty?.SetValue(__instance, true);
				return false;
			}

			if (IsPressedPropertyKeyField?.GetValue(null) is DependencyPropertyKey propertyKey)
				ClearValueMethod?.Invoke(__instance, new object[] { propertyKey });

			return false;
		}
	}

	[HarmonyPatch(typeof(RibbonMenuItem), "UpdateIsPressed")]
	public static class PatchRibbonMenuItemUpdateIsPressed
	{
		private static readonly PropertyInfo? IsPressedProperty = typeof(RibbonMenuItem).GetProperty("IsPressed", InvokeAllBindings);

		public static bool Prefix(RibbonMenuItem __instance)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			IsPressedProperty?.SetValue(__instance, Mouse.LeftButton == MouseButtonState.Pressed);
			return false;
		}
	}

	[HarmonyPatch(typeof(DataGridCheckBoxColumn), "IsMouseOver")]
	public static class PatchDataGridCheckBoxColumnIsMouseOver
	{
		public static bool Prefix(ref bool __result)
		{
			if (!IsSyntheticMouseInputActive)
				return true;

			__result = true;
			return false;
		}
	}

	private sealed class SyntheticInputScope : IDisposable
	{
		private readonly bool includeMouse;
		private readonly bool includeKeyboard;
		private int disposed;

		public SyntheticInputScope(bool includeMouse, bool includeKeyboard)
		{
			this.includeMouse = includeMouse;
			this.includeKeyboard = includeKeyboard;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0)
				return;

			Interlocked.Decrement(ref syntheticInputDepth);
			if (includeMouse)
				Interlocked.Decrement(ref syntheticMouseInputDepth);
			if (includeKeyboard && Interlocked.Decrement(ref syntheticKeyboardInputDepth) == 0)
				ResetKeyboardState();
		}
	}

	[HarmonyPatch(typeof(Window), "ShowDialog")]
	public static class PatchWindowShowDialog
	{
		public static bool Prefix()
		{
			ShowDialogCalled = true;
			return true;
		}
	}

	[HarmonyPatch]
	public static class PatchMessageBoxShow
	{
		public static IEnumerable<MethodBase> TargetMethods()
		{
			foreach (var method in GetMessageBoxShowMethods(typeof(MessageBox)))
				yield return method;

			foreach (var method in GetMessageBoxShowMethods(typeof(WinForms.MessageBox)))
				yield return method;
		}

		public static bool Prefix()
		{
			ShowDialogCalled = true;
			return true;
		}

		private static IEnumerable<MethodBase> GetMessageBoxShowMethods(Type type)
		{
			foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
			{
				if (method.Name == "Show")
					yield return method;
			}
		}
	}

	[HarmonyPatch]
	public static class PatchCommonDialogShowDialog
	{
		public static IEnumerable<MethodBase> TargetMethods()
		{
			foreach (var method in GetShowDialogMethods(typeof(CommonDialog)))
				yield return method;

			foreach (var method in GetShowDialogMethods(typeof(WinForms.CommonDialog)))
				yield return method;
		}

		public static bool Prefix()
		{
			ShowDialogCalled = true;
			return true;
		}

		private static IEnumerable<MethodBase> GetShowDialogMethods(Type type)
		{
			foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
			{
				if (method.Name != "ShowDialog")
					continue;

				var parameters = method.GetParameters();
				if (parameters.Length <= 1)
					yield return method;
			}
		}
	}

	private const BindingFlags InvokeAllBindings = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

	private static MethodInfo? GetBestMatchingMethod(Type type, string methodName, params object?[] args)
	{
		var methods = GetCandidateMethods(type, methodName, InvokeAllBindings, args);
		return methods.Count == 0 ? null : methods[0];
	}
}
