namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Linq;
using DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

internal static partial class TargetActionCommand
{
	private static readonly IUiTargetAdapter[] TargetAdapters =
	[
		new WpfTargetAdapter(),
		new WinFormsTargetAdapter(),
		new AutomationTargetAdapter(),
		new NativeHwndTargetAdapter(),
		new ReflectionTargetAdapter(),
	];

	private static ActionResult InvokeTargetAdapter(object target, Func<IUiTargetAdapter, ActionResult> action, string actionName)
	{
		var adapter = TargetAdapters.FirstOrDefault(adapter => adapter.CanHandle(target));
		return adapter is null
			? UnsupportedAdapterAction(target, actionName)
			: action(adapter);
	}

	private static bool EnsureForegroundTarget(object target)
	{
		var adapter = TargetAdapters.FirstOrDefault(adapter => adapter.CanHandle(target));
		return adapter?.TryEnsureForeground(target) == true;
	}

	private static ActionResult TypeTextIntoFocusedTarget(string text, bool clearFirst)
	{
		var focusedTarget = WpfTargetAdapter.GetFocusedTarget();
		if (focusedTarget is null)
		{
			TargetKeyboardInput.TypePhysical(text);
			return ActionResult.Ok();
		}

		return TypeTextIntoTarget(focusedTarget, text, clearFirst);
	}

	private static ActionResult SendKeysToForeground(object? keys, int delayMs) =>
		TargetKeyboardInput.SendKeysToForeground(keys, delayMs);

	internal static ActionResult UnsupportedAdapterAction(object target, string actionName) =>
		ActionResult.Unsupported($"Target type '{target.GetType().FullName}' does not support {actionName}.");
}
