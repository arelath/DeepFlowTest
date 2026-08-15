namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;

internal sealed class WpfTargetAdapter : UiTargetAdapterBase
{
	public override bool CanHandle(object target) =>
		target is IInputElement or DependencyObject;

	public override ActionResult Click(object target, MouseButtonKind button, int clickCount)
	{
		if (WpfControlOperations.TryClick(target, button, clickCount, out var result))
			return result;

		return target is UIElement uiElement
			? WpfPointerInput.Click(uiElement, button, clickCount)
			: base.Click(target, button, clickCount);
	}

	public override ActionResult MouseWheel(object target, int delta) =>
		target is UIElement uiElement
			? WpfPointerInput.MouseWheel(uiElement, delta)
			: base.MouseWheel(target, delta);

	public override ActionResult Focus(object target) =>
		WpfWindowActivation.Focus(target);

	public override ActionResult TypeText(object target, string text, bool clearFirst) =>
		WpfKeyboardInput.TypeText(target, text, clearFirst, () => base.TypeText(target, text, clearFirst));

	public override ActionResult SendKeys(object target, object? keys, string keyText, int delayMs) =>
		WpfKeyboardInput.SendKeys(target, keys, delayMs, () => base.SendKeys(target, keys, keyText, delayMs));

	public override bool TryEnsureForeground(object target) =>
		WpfWindowActivation.TryEnsureForeground(target);

	public override PointerTargetResult GetPointerTarget(object target, PointerAnchor anchor) =>
		target is UIElement uiElement
			? WpfPointerInput.GetPointerTarget(uiElement, anchor)
			: base.GetPointerTarget(target, anchor);

	public override ActionResult SetProperty(object target, string propertyName, object? value) =>
		WpfPropertyAccessor.SetProperty(target, propertyName, value);

	public override ActionResult RaiseKnownRoutedEvent(object target, string eventName) =>
		WpfRoutedEventInvoker.RaiseKnown(target, eventName, () => base.RaiseKnownRoutedEvent(target, eventName));

	public override ActionResult RaiseExpressionRoutedEvent(object target, object? expressionPayload, int? timeoutMs) =>
		WpfRoutedEventInvoker.RaiseExpression(
			target,
			expressionPayload,
			timeoutMs,
			() => base.RaiseExpressionRoutedEvent(target, expressionPayload, timeoutMs));

	public override ActionResult RunKnownOperation(object target, string? operation) =>
		WpfControlOperations.TryRun(target, operation, out var result)
			? result
			: base.RunKnownOperation(target, operation);
}
