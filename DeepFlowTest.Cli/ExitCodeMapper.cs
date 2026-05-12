namespace DeepFlowTest.Cli;

using System;

public static class ExitCodeMapper
{
	public static int Map(string? errorCode)
	{
		if (string.IsNullOrEmpty(errorCode))
			return 0;

		return errorCode switch
		{
			CliErrorCodes.InvalidArguments => 1,
			CliErrorCodes.InvalidConfig => 1,
			CliErrorCodes.ActionDenied => 1,
			CliErrorCodes.ArbitraryInvokeDenied => 1,
			CliErrorCodes.TargetNotFound => 2,
			CliErrorCodes.AmbiguousTarget => 2,
			CliErrorCodes.UnsupportedTarget => 3,
			CliErrorCodes.AttachFailed => 4,
			CliErrorCodes.PipeFailed => 5,
			CliErrorCodes.ProtocolError => 5,
			CliErrorCodes.CommandTimeout => 6,
			CliErrorCodes.TargetExited => 7,
			CliErrorCodes.NoMatch => 8,
			CliErrorCodes.StaleTarget => 8,
			CliErrorCodes.UnexpectedError => 9,
			CliErrorCodes.PipeBusy => 10,
			CliErrorCodes.NotImplemented => 1,
			_ => 9,
		};
	}

	public static string FromException(Exception exception)
	{
		return exception switch
		{
			CliException cliException => cliException.ErrorCode,
			_ => CliErrorCodes.UnexpectedError,
		};
	}
}
