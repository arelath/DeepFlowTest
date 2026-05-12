namespace DeepFlowTest.Cli;

using System;

public sealed class CliException : Exception
{
	public CliException(string errorCode, string message, object? details = null)
		: base(message)
	{
		ErrorCode = errorCode;
		Details = details;
	}

	public string ErrorCode { get; }

	public object? Details { get; }
}

public static class CliErrorCodes
{
	public const string ActionDenied = "action-denied";
	public const string AmbiguousTarget = "ambiguous-target";
	public const string ArbitraryInvokeDenied = "arbitrary-invoke-denied";
	public const string AttachFailed = "attach-failed";
	public const string CommandTimeout = "command-timeout";
	public const string InvalidArguments = "invalid-arguments";
	public const string InvalidConfig = "invalid-config";
	public const string NoMatch = "no-match";
	public const string NotImplemented = "not-implemented";
	public const string PipeBusy = "pipe-busy";
	public const string PipeFailed = "pipe-failed";
	public const string ProtocolError = "protocol-error";
	public const string StaleTarget = "stale-target";
	public const string TargetExited = "target-exited";
	public const string TargetNotFound = "target-not-found";
	public const string UnexpectedError = "unexpected-error";
	public const string UnsupportedArchitecture = "unsupported-architecture";
	public const string UnsupportedFramework = "unsupported-framework";
	public const string UnsupportedTarget = "unsupported-target";
}
