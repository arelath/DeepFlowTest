namespace DeepFlowTest.AppDriverPayload.Commands.TargetActions;

using System;
using System.Globalization;
using System.Threading;
using DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class KeyboardTargetActionHandler
{
	public static object TypeText(TypeTextCommandRequest request, TreeService treeService)
	{
		if (!string.IsNullOrWhiteSpace(request.TargetId))
		{
			return TargetActionPipeline.Execute(ProtocolConstants.Commands.TypeText, request.TargetId!, treeService, target =>
				UiTargetAdapterRouter.Invoke(
					target,
					adapter => adapter.TypeText(target, request.Text ?? string.Empty, request.ClearFirst),
					"text input"));
		}

		return TargetActionPipeline.ExecuteUntargeted(() => TypeTextIntoFocusedTarget(request.Text, request.ClearFirst));
	}

	public static object KeyPress(KeyPressCommandRequest request, TreeService treeService)
	{
		if (!string.IsNullOrWhiteSpace(request.TargetId))
		{
			return TargetActionPipeline.Execute(ProtocolConstants.Commands.KeyPress, request.TargetId!, treeService, target =>
				SendKeysToTarget(target, request.Keys, request.DelayMs, request.EnsureForeground));
		}

		return TargetActionPipeline.ExecuteUntargeted(() => TargetKeyboardInput.SendKeysToForeground(request.Keys, request.DelayMs));
	}

	private static ActionResult TypeTextIntoFocusedTarget(string text, bool clearFirst)
	{
		var focusedTarget = WpfKeyboardInput.GetFocusedTarget();
		if (focusedTarget is null)
		{
			TargetKeyboardInput.TypePhysical(text);
			return ActionResult.Ok();
		}

		return UiTargetAdapterRouter.Invoke(
			focusedTarget,
			adapter => adapter.TypeText(focusedTarget, text ?? string.Empty, clearFirst),
			"text input");
	}

	private static ActionResult SendKeysToTarget(object target, object? keys, int delayMs, bool ensureForeground)
	{
		_ = ensureForeground;
		var keyText = Convert.ToString(TargetValueConverter.UnwrapJsonValue(keys), CultureInfo.InvariantCulture) ?? string.Empty;
		if (string.IsNullOrEmpty(keyText))
			return ActionResult.Unsupported("Key input cannot be empty.");

		if (delayMs > 0)
			Thread.Sleep(delayMs);

		return UiTargetAdapterRouter.Invoke(
			target,
			adapter => adapter.SendKeys(target, keys, keyText, delayMs),
			"key input");
	}
}
