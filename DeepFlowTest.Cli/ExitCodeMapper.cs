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
			AutomationErrorCodes.InvalidArguments => 1,
			AutomationErrorCodes.InvalidConfig => 1,
			AutomationErrorCodes.ActionDenied => 1,
			AutomationErrorCodes.ArbitraryInvokeDenied => 1,
			AutomationErrorCodes.TargetNotFound => 2,
			AutomationErrorCodes.AmbiguousTarget => 2,
			AutomationErrorCodes.UnsupportedTarget => 3,
			AutomationErrorCodes.UnsupportedFramework => 3,
			AutomationErrorCodes.UnsupportedArchitecture => 3,
			AutomationErrorCodes.AttachFailed => 4,
			AutomationErrorCodes.PipeFailed => 5,
			AutomationErrorCodes.ProtocolError => 5,
			AutomationErrorCodes.CommandTimeout => 6,
			AutomationErrorCodes.TargetExited => 7,
			AutomationErrorCodes.NoMatch => 8,
			AutomationErrorCodes.StaleTarget => 8,
			AutomationErrorCodes.UnexpectedError => 9,
			AutomationErrorCodes.PipeBusy => 10,
			AutomationErrorCodes.NotImplemented => 1,
			_ => 9,
		};
	}

	public static string FromException(Exception exception)
	{
		return exception switch
		{
			AutomationException cliException => cliException.ErrorCode,
			_ => AutomationErrorCodes.UnexpectedError,
		};
	}
}
