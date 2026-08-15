namespace DeepFlowTest.AppDriverPayload.Commands.TargetActions;

using System;
using System.Globalization;
using DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class RoutedEventTargetActionHandler
{
	public static object RaiseEvent(RaiseEventCommandRequest request, TreeService treeService) =>
		TargetActionPipeline.Execute(ProtocolConstants.Commands.RaiseEvent, request.TargetId, treeService, target =>
		{
			if (TargetExpressionEvaluator.CanEvaluate(request.GetRoutedEventArgs))
			{
				return UiTargetAdapterRouter.Invoke(
					target,
					adapter => adapter.RaiseExpressionRoutedEvent(target, request.GetRoutedEventArgs, request.TimeoutMs),
					"routed event expression");
			}

			var eventName = !string.IsNullOrWhiteSpace(request.EventName)
				? request.EventName
				: Convert.ToString(request.GetRoutedEventArgs, CultureInfo.InvariantCulture) ?? string.Empty;
			return RaiseKnown(target, eventName);
		});

	public static object KnownRoutedEvent(KnownRoutedEventCommandRequest request, TreeService treeService) =>
		TargetActionPipeline.Execute(ProtocolConstants.Commands.KnownRoutedEvent, request.TargetId, treeService, target =>
			RaiseKnown(target, request.EventName));

	private static ActionResult RaiseKnown(object target, string eventName) =>
		UiTargetAdapterRouter.Invoke(
			target,
			adapter => adapter.RaiseKnownRoutedEvent(target, eventName),
			$"routed event '{eventName}'");
}
