namespace DeepFlowTest.AppDriverPayload.Commands;

using DeepFlowTest.Contracts;

internal static class StopSendingCommand
{
	public static object Process(StopSendingCommandRequest request, ReusablePipeSession? reusableSession)
	{
		if (request is null || string.IsNullOrWhiteSpace(request.SubscriptionId))
		{
			return StandardIpcResponse.FromError(
				"Stop stream request requires a subscription ID.",
				ProtocolConstants.ErrorCodes.InvalidArguments,
				PayloadLog.CurrentCorrelationId);
		}

		if (reusableSession is null)
		{
			return StandardIpcResponse.FromError(
				"Streaming requires a reusable pipe session.",
				ProtocolConstants.ErrorCodes.ProtocolError,
				PayloadLog.CurrentCorrelationId);
		}

		var stopped = reusableSession.StopSubscription(request.SubscriptionId, request.TimeoutMs ?? 2000);
		return new StopSendingCommandResponse
		{
			SubscriptionId = request.SubscriptionId,
			Status = stopped ? ProtocolConstants.Statuses.Stopped : ProtocolConstants.Statuses.UnknownSubscription,
		};
	}

}
