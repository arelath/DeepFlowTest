namespace DeepFlowTest;

using System;

public sealed class AppDriverException : Exception
{
	public AppDriverException(string errorCode, string message)
		: base(message)
	{
		ErrorCode = errorCode;
	}

	public AppDriverException(string errorCode, string message, Exception innerException)
		: base(message, innerException)
	{
		ErrorCode = errorCode;
	}

	public string ErrorCode { get; }
}

public static class AppDriverErrorCodes
{
	public const string AmbiguousTarget = "ambiguous-target";
	public const string InjectorFailed = "injector-failed";
	public const string TargetNotFound = "target-not-found";
}
