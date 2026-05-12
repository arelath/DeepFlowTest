namespace DeepFlowTest.Cli;

using System;

public static class CliPipeName
{
	public const string Prefix = "deepflowtest-cli-v1";

	public static string ForTarget(int processId, string? pipeId = null)
	{
		if (processId <= 0)
			throw new CliException(CliErrorCodes.InvalidArguments, "Process ID must be positive.");

		if (pipeId is null)
			return $"{Prefix}-pid-{processId}";

		ValidatePipeId(pipeId);
		return $"{Prefix}-{pipeId}";
	}

	public static void ValidatePipeId(string pipeId)
	{
		if (string.IsNullOrWhiteSpace(pipeId))
			throw new CliException(CliErrorCodes.InvalidArguments, "Pipe ID must not be empty.");

		if (pipeId.IndexOfAny(new[] { ' ', '/', '\\', ':' }) >= 0)
			throw new CliException(CliErrorCodes.InvalidArguments, "Pipe ID must not contain spaces, slash, backslash, or colon.");
	}
}
