namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;

internal interface IUiTargetAdapter
{
	bool CanHandle(object target);

	ActionResult Click(object target, MouseButtonKind button, int clickCount);

	ActionResult MouseWheel(object target, int delta);

	ActionResult Focus(object target);

	ActionResult TypeText(object target, string text, bool clearFirst);

	ActionResult SendKeys(object target, object? keys, string keyText, int delayMs);

	bool TryEnsureForeground(object target);

	PointerTargetResult GetPointerTarget(object target, PointerAnchor anchor);

	ActionResult SetProperty(object target, string propertyName, object? value);

	ActionResult RaiseKnownRoutedEvent(object target, string eventName);

	ActionResult RaiseExpressionRoutedEvent(object target, object? expressionPayload, int? timeoutMs);

	ActionResult RunKnownOperation(object target, string? operation);
}
