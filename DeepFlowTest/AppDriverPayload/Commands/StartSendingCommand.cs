namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class StartSendingCommand
{
	public static object Process(
		StartSendingCommandRequest request,
		NamedPipeServer.Command command,
		ReusablePipeSession? reusableSession,
		TreeService treeService)
	{
		var validation = Validate(request);
		if (validation is not null)
			return validation;

		if (reusableSession is null)
		{
			return StandardIpcResponse.FromError(
				"Streaming requires a reusable pipe session.",
				ProtocolConstants.ErrorCodes.ProtocolError,
				PayloadLog.CurrentCorrelationId);
		}

		var targetValidation = ValidateTarget(request, treeService);
		if (targetValidation is not null)
			return targetValidation;

		var subscription = reusableSession.StartSubscription(
			request.StreamKind,
			command.ConnectionId,
			request.IntervalMs,
			command.TrySend ?? (_ => false),
			CreateCapture(request, treeService),
			deferStart: true);
		var response = new StartSendingCommandResponse
		{
			SubscriptionId = subscription.SubscriptionId,
			StreamKind = subscription.Kind,
			Status = ProtocolConstants.Statuses.Started,
			IntervalMs = subscription.IntervalMs,
			SequenceStart = 1,
		};
		command.HoldConnectionOpen?.Invoke();
		command.Respond(response);
		reusableSession.StartStoredSubscription(subscription.SubscriptionId);
		return response;
	}

	private static StandardIpcResponse? Validate(StartSendingCommandRequest request)
	{
		if (request is null)
			return StandardIpcResponse.FromError("Start stream request is required.", ProtocolConstants.ErrorCodes.ProtocolError, PayloadLog.CurrentCorrelationId);

		if (request.StreamKind is not (ProtocolConstants.StreamKinds.VisualTree
			or ProtocolConstants.StreamKinds.VisualTreeDelta
			or ProtocolConstants.StreamKinds.Screenshot
			or ProtocolConstants.StreamKinds.EventLog))
		{
			return StandardIpcResponse.FromError($"Unsupported stream kind '{request.StreamKind}'.", ProtocolConstants.ErrorCodes.InvalidArguments, PayloadLog.CurrentCorrelationId);
		}

		if (request.IntervalMs < 50)
			return StandardIpcResponse.FromError("Stream interval must be at least 50 ms.", ProtocolConstants.ErrorCodes.InvalidArguments, PayloadLog.CurrentCorrelationId);

		if (!Enum.IsDefined(typeof(DeepFlowTest.ImageFormat), request.Format))
			return StandardIpcResponse.FromError($"Unsupported stream image format '{request.Format}'.", ProtocolConstants.ErrorCodes.InvalidArguments, PayloadLog.CurrentCorrelationId);

		if (request.PropNames is not null)
		{
			foreach (var property in request.PropNames)
				if (string.IsNullOrWhiteSpace(property))
					return StandardIpcResponse.FromError("Stream property names cannot be blank.", ProtocolConstants.ErrorCodes.InvalidArguments, PayloadLog.CurrentCorrelationId);
		}

		return null;
	}

	private static StandardIpcResponse? ValidateTarget(StartSendingCommandRequest request, TreeService treeService)
	{
		if (string.IsNullOrWhiteSpace(request.TargetId))
			return null;

		var resolution = treeService.ResolveTarget(request.TargetId!);
		if (resolution.Status == TargetIdResolutionStatus.Found)
			return null;

		var errorCode = resolution.Status == TargetIdResolutionStatus.Stale
			? ProtocolConstants.ErrorCodes.StaleTarget
			: ProtocolConstants.ErrorCodes.UnsupportedTarget;
		return StandardIpcResponse.FromError($"Target '{request.TargetId}' resolved as {resolution.Status}.", errorCode, PayloadLog.CurrentCorrelationId);
	}

	private static Func<long, object> CreateCapture(StartSendingCommandRequest request, TreeService treeService)
	{
		VisualTreeSnapshot? previous = null;
		var imageFormat = request.Format;
		if (request.StreamKind == ProtocolConstants.StreamKinds.EventLog)
			return _ => CaptureEventLog(treeService);

		return _ =>
		{
			object? captured = null;
			var runResult = ThreadUtility.RunOnUIThreadAsync(() =>
				{
					captured = request.StreamKind switch
					{
						ProtocolConstants.StreamKinds.VisualTree => CaptureSnapshot(request, treeService),
						ProtocolConstants.StreamKinds.VisualTreeDelta => CaptureDelta(request, treeService, ref previous),
						ProtocolConstants.StreamKinds.Screenshot => ScreenshotCommand.Process(
							new ScreenshotCommandRequest
							{
								Format = imageFormat,
								TargetId = request.TargetId,
								TimeoutMs = request.TimeoutMs,
							},
							treeService),
						_ => throw new InvalidOperationException($"Unsupported stream kind '{request.StreamKind}'."),
					};
					return System.Threading.Tasks.Task.CompletedTask;
				})
				.ConfigureAwait(false)
				.GetAwaiter()
				.GetResult();

			return runResult == UiThreadRunResult.Finished
				? captured ?? StandardIpcResponse.Ok()
				: StandardIpcResponse.FromError("No supported UI thread is available for streaming.", ProtocolConstants.ErrorCodes.UnsupportedTarget, PayloadLog.CurrentCorrelationId);
		};
	}

	private static object CaptureSnapshot(StartSendingCommandRequest request, TreeService treeService) =>
		treeService.CaptureSnapshot(new TreeSnapshotOptions
		{
			RequestedPropertyNames = request.PropNames,
			RootTargetId = request.TargetId,
			IncludeHidden = true,
			MaxNodeCount = 1000,
		});

	private static object CaptureDelta(StartSendingCommandRequest request, TreeService treeService, ref VisualTreeSnapshot? previous)
	{
		var current = (VisualTreeSnapshot)CaptureSnapshot(request, treeService);
		if (previous is null)
		{
			previous = current;
			return new VisualTreeDeltaSnapshotFrame(current);
		}

		var delta = VisualTreeSnapshotDelta.Create(previous, current);
		previous = current;
		return delta;
	}

	private static object CaptureEventLog(TreeService treeService)
	{
		var snapshot = treeService.CaptureSnapshot(new TreeSnapshotOptions
		{
			RequestedPropertyNames = ["Name", "AutomationProperties.Name"],
			IncludeHidden = true,
			MaxNodeCount = 50,
		});
		return new
		{
			status = "heartbeat",
			processId = System.Diagnostics.Process.GetCurrentProcess().Id,
			rootCount = snapshot.RootIds.Count,
			roots = snapshot.RootIds,
			generatedUtc = DateTimeOffset.UtcNow,
		};
	}

}
