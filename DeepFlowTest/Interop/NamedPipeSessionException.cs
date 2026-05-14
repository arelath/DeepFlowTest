namespace DeepFlowTest.Interop;

using System;

public sealed class NamedPipeSessionException : Exception
{
	public NamedPipeSessionException(string errorCode, string message, int? targetExitCode = null, string? crashLog = null)
		: base(message)
	{
		ErrorCode = errorCode;
		TargetExitCode = targetExitCode;
		CrashLog = crashLog;
	}

	public NamedPipeSessionException(string errorCode, string message, Exception innerException, int? targetExitCode = null, string? crashLog = null)
		: base(message, innerException)
	{
		ErrorCode = errorCode;
		TargetExitCode = targetExitCode;
		CrashLog = crashLog;
	}

	public string ErrorCode { get; }

	public int? TargetExitCode { get; }

	public string? CrashLog { get; }
}
