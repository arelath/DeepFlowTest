namespace DeepFlowTest.Cli;

using DeepFlowTest.Contracts;

public static class ProtocolErrorMapper
{
	public static string Map(string? errorCode)
	{
		return errorCode switch
		{
			ProtocolConstants.ErrorCodes.InvalidArguments => CliErrorCodes.InvalidArguments,
			ProtocolConstants.ErrorCodes.StaleTarget => CliErrorCodes.StaleTarget,
			ProtocolConstants.ErrorCodes.TargetExited => CliErrorCodes.TargetExited,
			ProtocolConstants.ErrorCodes.UnsupportedTarget => CliErrorCodes.UnsupportedTarget,
			ProtocolConstants.ErrorCodes.CommandTimeout => CliErrorCodes.CommandTimeout,
			ProtocolConstants.ErrorCodes.UnsupportedCommand => CliErrorCodes.UnsupportedTarget,
			_ => CliErrorCodes.ProtocolError,
		};
	}
}
