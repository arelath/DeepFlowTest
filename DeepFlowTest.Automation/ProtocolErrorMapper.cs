namespace DeepFlowTest.Automation;

using DeepFlowTest.Contracts;

public static class ProtocolErrorMapper
{
	public static string Map(string? errorCode)
	{
		return errorCode switch
		{
			ProtocolConstants.ErrorCodes.InvalidArguments => AutomationErrorCodes.InvalidArguments,
			ProtocolConstants.ErrorCodes.StaleTarget => AutomationErrorCodes.StaleTarget,
			ProtocolConstants.ErrorCodes.TargetExited => AutomationErrorCodes.TargetExited,
			ProtocolConstants.ErrorCodes.UnsupportedTarget => AutomationErrorCodes.UnsupportedTarget,
			ProtocolConstants.ErrorCodes.CommandTimeout => AutomationErrorCodes.CommandTimeout,
			ProtocolConstants.ErrorCodes.UnsupportedCommand => AutomationErrorCodes.UnsupportedTarget,
			_ => AutomationErrorCodes.ProtocolError,
		};
	}
}
