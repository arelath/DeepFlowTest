namespace DeepFlowTest.AppDriverPayload.Commands.TargetActions;

using DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class ControlTargetActionHandler
{
	public static object Focus(FocusCommandRequest request, TreeService treeService) =>
		TargetActionPipeline.Execute(ProtocolConstants.Commands.Focus, request.TargetId, treeService, target =>
			UiTargetAdapterRouter.Invoke(target, adapter => adapter.Focus(target), "focus"));

	public static object KnownOperation(KnownOperationCommandRequest request, TreeService treeService) =>
		TargetActionPipeline.Execute(ProtocolConstants.Commands.KnownOperation, request.TargetId, treeService, target =>
			UiTargetAdapterRouter.Invoke(
				target,
				adapter => adapter.RunKnownOperation(target, request.Operation),
				$"known operation '{request.Operation}'"));
}
