namespace DeepFlowTest.AppDriverPayload.Commands.TargetActions;

using DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class PropertyTargetActionHandler
{
	public static object SetProperty(SetPropertyCommandRequest request, TreeService treeService) =>
		TargetActionPipeline.Execute(ProtocolConstants.Commands.SetProperty, request.TargetId, treeService, target =>
		{
			if (string.IsNullOrWhiteSpace(request.PropertyName))
				return ActionResult.Unsupported("Property name is required.");

			var value = TargetExpressionEvaluator.CanEvaluate(request.PropertyValue)
				? TargetExpressionEvaluator.Evaluate(target, request.PropertyValue, timeoutMs: null, awaitTasks: true)
				: TargetValueConverter.UnwrapJsonValue(request.PropertyValue);
			return UiTargetAdapterRouter.Invoke(
				target,
				adapter => adapter.SetProperty(target, request.PropertyName, value),
				$"set property '{request.PropertyName}'");
		});
}
