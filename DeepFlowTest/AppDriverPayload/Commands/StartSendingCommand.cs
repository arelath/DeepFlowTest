namespace DeepFlowTest.AppDriverPayload.Commands;

using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal static class StartSendingCommand
{
	public static object Process(StartSendingCommandRequest request, NamedPipeServer.Command command, ReusablePipeSession? reusableSession)
	{
		var validation = Validate(request);
		if (validation is not null)
			return validation;

		if (reusableSession is null)
		{
			return StandardIpcResponse.FromError(
				"Streaming requires a reusable pipe session.",
				ProtocolConstants.ErrorCodes.ProtocolError,
				LogCorrelationId());
		}

		command.HoldConnectionOpen?.Invoke();
		var subscription = reusableSession.StartSubscription(request.StreamKind, command.ConnectionId, request.IntervalMs);
		return new StartSendingCommandResponse
		{
			SubscriptionId = subscription.SubscriptionId,
			StreamKind = subscription.Kind,
			Status = ProtocolConstants.Statuses.Started,
			IntervalMs = subscription.IntervalMs,
			SequenceStart = 1,
		};
	}

	private static StandardIpcResponse? Validate(StartSendingCommandRequest request)
	{
		if (request is null)
			return StandardIpcResponse.FromError("Start stream request is required.", ProtocolConstants.ErrorCodes.ProtocolError, LogCorrelationId());

		if (request.StreamKind is not (ProtocolConstants.StreamKinds.VisualTree
			or ProtocolConstants.StreamKinds.VisualTreeDelta
			or ProtocolConstants.StreamKinds.Screenshot
			or ProtocolConstants.StreamKinds.EventLog))
		{
			return StandardIpcResponse.FromError($"Unsupported stream kind '{request.StreamKind}'.", ProtocolConstants.ErrorCodes.ProtocolError, LogCorrelationId());
		}

		if (request.IntervalMs < 50)
			return StandardIpcResponse.FromError("Stream interval must be at least 50 ms.", ProtocolConstants.ErrorCodes.ProtocolError, LogCorrelationId());

		if (request.Format is not ("png" or "bmp" or "gif" or "jpg" or "jpeg"))
			return StandardIpcResponse.FromError($"Unsupported stream image format '{request.Format}'.", ProtocolConstants.ErrorCodes.ProtocolError, LogCorrelationId());

		return null;
	}

	private static string LogCorrelationId()
	{
		return System.IO.Path.GetFileNameWithoutExtension(PayloadLog.CurrentLogPath);
	}
}
