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
using DeepFlowTest.AppDriverPayload.Patching;
using HarmonyLib;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

public static class AppHooks
{
	private static readonly object Gate = new();
	private static WpfPatchResult lastResult = new() { FrameworkFamily = RuntimeFrameworkFamilies.Unknown };
	private static bool hooksApplied;
	private static volatile bool showDialogCalled;
	private static int syntheticMouseInputDepth;
	private static int syntheticInputDepth;

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
		var result = (coordinator ?? RuntimeWpfPatchCoordinator.Default).ApplyCurrentRuntime(log);
		lock (Gate)
			lastResult = result;
		return result;
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
	}

	public static bool IsSyntheticMouseInputActive => Volatile.Read(ref syntheticMouseInputDepth) > 0;

	public static bool IsSyntheticInputActive => Volatile.Read(ref syntheticInputDepth) > 0;

	public static IDisposable BeginSyntheticInput()
	{
		Interlocked.Increment(ref syntheticInputDepth);
		return new SyntheticInputScope(includeMouse: false);
	}

	public static IDisposable BeginSyntheticMouseInput()
	{
		Interlocked.Increment(ref syntheticInputDepth);
		Interlocked.Increment(ref syntheticMouseInputDepth);
		return new SyntheticInputScope(includeMouse: true);
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
		Volatile.Write(ref syntheticMouseInputDepth, 0);
		Volatile.Write(ref syntheticInputDepth, 0);
	}

	private static void WarmupHookedMembers()
	{
		TryWarmup(() => _ = Mouse.PrimaryDevice.LeftButton);
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
		private int disposed;

		public SyntheticInputScope(bool includeMouse)
		{
			this.includeMouse = includeMouse;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0)
				return;

			Interlocked.Decrement(ref syntheticInputDepth);
			if (includeMouse)
				Interlocked.Decrement(ref syntheticMouseInputDepth);
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
