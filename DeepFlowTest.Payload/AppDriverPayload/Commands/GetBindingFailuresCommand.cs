namespace DeepFlowTest.AppDriverPayload.Commands;

using DeepFlowTest.AppDriverPayload.Diagnostics;
using DeepFlowTest.Contracts;

internal static class GetBindingFailuresCommand
{
	public static object Process(GetBindingFailuresCommandRequest request)
	{
		if (request is null)
			return StandardIpcResponse.FromError("Binding failure request is required.", ProtocolConstants.ErrorCodes.ProtocolError, PayloadLog.CurrentCorrelationId);

		if (request.MaxCount < 0)
			return StandardIpcResponse.FromError("Binding failure max count must be zero or greater.", ProtocolConstants.ErrorCodes.InvalidArguments, PayloadLog.CurrentCorrelationId);

		return BindingFailureCaptureService.Instance.ReadSince(request.AfterSequenceNumber, request.MaxCount);
	}
}
