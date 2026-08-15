namespace DeepFlowTest.AppDriverPayload.Commands.TargetActions;

using System;
using System.Collections.Generic;
using System.Windows;
using DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class PointerTargetActionHandler
{
	public static object Click(ClickCommandRequest request, TreeService treeService) =>
		TargetActionPipeline.Execute(ProtocolConstants.Commands.Click, request.TargetId, treeService, target =>
		{
			var buttonName = ProtocolValueMapper.FormatMouseButton(request.MouseButton);
			return UiTargetAdapterRouter.Invoke(
				target,
				adapter => adapter.Click(target, request.MouseButton, request.ClickCount),
				$"{buttonName} click");
		});

	public static object MouseWheel(MouseWheelCommandRequest request, TreeService treeService)
	{
		if (request.Delta == 0)
			return StandardIpcResponse.FromError("Mouse wheel delta must not be zero.", ProtocolConstants.ErrorCodes.InvalidArguments, PayloadLog.CurrentCorrelationId);

		return TargetActionPipeline.Execute(ProtocolConstants.Commands.MouseWheel, request.TargetId, treeService, target =>
			UiTargetAdapterRouter.Invoke(target, adapter => adapter.MouseWheel(target, request.Delta), "mouse wheel"));
	}

	public static object DragAndDrop(DragAndDropCommandRequest request, TreeService treeService)
	{
		var validationError = ValidateDragAndDropRequest(request);
		if (validationError is not null)
			return StandardIpcResponse.FromError(validationError, ProtocolConstants.ErrorCodes.InvalidArguments, PayloadLog.CurrentCorrelationId);

		IReadOnlyList<TargetActionTarget> targetRequests =
		[
			TargetActionTarget.DragRole(request.TargetId, "source"),
			TargetActionTarget.DragRole(request.DestinationTargetId, "destination"),
		];
		return TargetActionPipeline.Execute(ProtocolConstants.Commands.DragAndDrop, treeService, targetRequests, targets =>
		{
			var sourceTarget = targets[0];
			var destinationTarget = targets[1];
			if (request.UseInjectedEvents && sourceTarget is UIElement && destinationTarget is UIElement)
			{
				var result = WpfDragDropSimulator.PerformInjected(
					sourceTarget,
					destinationTarget,
					new PointerAnchor(request.SourceAnchorX, request.SourceAnchorY),
					new PointerAnchor(request.DestinationAnchorX, request.DestinationAnchorY),
					request.DurationMs,
					request.StepIntervalMs);
				if (!result.Success && IsDetachedPresentationSourceError(result.Error))
					result = ActionResult.Failure(result.Error!, ProtocolConstants.ErrorCodes.StaleTarget);

				return TargetActionOutcome.Completed(result);
			}

			var sourcePoint = UiTargetAdapterRouter.GetPointerTarget(
				sourceTarget,
				new PointerAnchor(request.SourceAnchorX, request.SourceAnchorY));
			if (!sourcePoint.Success || sourcePoint.Value is null)
			{
				return TargetActionOutcome.Completed(ActionResult.Failure(
					$"{ProtocolConstants.Commands.DragAndDrop}: source target '{request.TargetId}': {sourcePoint.Error ?? "Screen coordinates could not be resolved."}",
					ProtocolConstants.ErrorCodes.UnsupportedTarget,
					formatErrorContext: false));
			}

			var destinationPoint = UiTargetAdapterRouter.GetPointerTarget(
				destinationTarget,
				new PointerAnchor(request.DestinationAnchorX, request.DestinationAnchorY));
			if (!destinationPoint.Success || destinationPoint.Value is null)
			{
				return TargetActionOutcome.Completed(ActionResult.Failure(
					$"{ProtocolConstants.Commands.DragAndDrop}: destination target '{request.DestinationTargetId}': {destinationPoint.Error ?? "Screen coordinates could not be resolved."}",
					ProtocolConstants.ErrorCodes.UnsupportedTarget,
					formatErrorContext: false));
			}

			var plan = new DragPlan(
				sourcePoint.Value,
				destinationPoint.Value,
				request.DurationMs,
				request.HoldMs,
				request.StepIntervalMs,
				request.PostDropWaitMs,
				request.EnsureForeground,
				request.ValidateSameProcess);
			return TargetActionOutcome.Defer(cancellationToken => TargetMouseInput.PerformDragAndDrop(plan, cancellationToken));
		});
	}

	private static bool IsDetachedPresentationSourceError(string? error) =>
		error?.IndexOf("not connected to a PresentationSource", StringComparison.OrdinalIgnoreCase) >= 0;

	private static string? ValidateDragAndDropRequest(DragAndDropCommandRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.TargetId))
			return "Drag and drop requires a source target ID.";
		if (string.IsNullOrWhiteSpace(request.DestinationTargetId))
			return "Drag and drop requires a destination target ID.";
		if (request.DurationMs is < 0 or > 15_000)
			return "Drag duration must be between 0 and 15000 ms.";
		if (request.HoldMs is < 0 or > 5_000)
			return "Drag hold time must be between 0 and 5000 ms.";
		if (request.StepIntervalMs is < 1 or > 250)
			return "Drag step interval must be between 1 and 250 ms.";
		if (request.PostDropWaitMs is < 0 or > 5_000)
			return "Post-drop wait must be between 0 and 5000 ms.";
		if (!IsValidAnchor(request.SourceAnchorX) || !IsValidAnchor(request.SourceAnchorY) ||
			!IsValidAnchor(request.DestinationAnchorX) || !IsValidAnchor(request.DestinationAnchorY))
		{
			return "Drag anchors must be finite values between 0.0 and 1.0.";
		}

		return null;
	}

	private static bool IsValidAnchor(double value) =>
		value >= 0 && value <= 1 && !double.IsNaN(value) && !double.IsInfinity(value);
}
