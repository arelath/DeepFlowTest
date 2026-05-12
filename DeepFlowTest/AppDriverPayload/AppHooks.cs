namespace DeepFlowTest.AppDriverPayload;

using System;
using DeepFlowTest.AppDriverPayload.Patching;

public static class AppHooks
{
	private static readonly object Gate = new();
	private static WpfPatchResult lastResult = new() { FrameworkFamily = RuntimeFrameworkFamilies.Unknown };

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

	public static void ResetForTests()
	{
		lock (Gate)
			lastResult = new WpfPatchResult { FrameworkFamily = RuntimeFrameworkFamilies.Unknown };
	}
}
