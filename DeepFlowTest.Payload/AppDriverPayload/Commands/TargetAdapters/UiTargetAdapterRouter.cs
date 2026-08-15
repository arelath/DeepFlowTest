namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Linq;
using DeepFlowTest.AppDriverPayload.Commands;

internal static class UiTargetAdapterRouter
{
	private static readonly IUiTargetAdapter[] TargetAdapters =
	[
		new WpfTargetAdapter(),
		new WinFormsTargetAdapter(),
		new AutomationTargetAdapter(),
		new NativeHwndTargetAdapter(),
		new ReflectionTargetAdapter(),
	];

	public static ActionResult Invoke(object target, Func<IUiTargetAdapter, ActionResult> action, string actionName)
	{
		var adapter = Find(target);
		return adapter is null
			? UnsupportedAction(target, actionName)
			: action(adapter);
	}

	public static bool EnsureForeground(object target) =>
		Find(target)?.TryEnsureForeground(target) == true;

	public static PointerTargetResult GetPointerTarget(object target, PointerAnchor anchor)
	{
		var adapter = Find(target);
		return adapter is null
			? PointerTargetResult.Unsupported($"Target type '{target.GetType().FullName}' cannot be converted to screen coordinates.")
			: adapter.GetPointerTarget(target, anchor);
	}

	public static ActionResult UnsupportedAction(object target, string actionName) =>
		ActionResult.Unsupported($"Target type '{target.GetType().FullName}' does not support {actionName}.");

	private static IUiTargetAdapter? Find(object target) =>
		TargetAdapters.FirstOrDefault(adapter => adapter.CanHandle(target));
}
