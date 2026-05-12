namespace DeepFlowTest.InjectorLauncher;

using System;

internal sealed class InjectorLauncherException : Exception
{
	public InjectorLauncherException(int exitCode, string message)
		: base(message)
	{
		ExitCode = exitCode;
	}

	public InjectorLauncherException(int exitCode, string message, Exception innerException)
		: base(message, innerException)
	{
		ExitCode = exitCode;
	}

	public int ExitCode { get; }
}

internal static class InjectorExitCode
{
	public const int Success = 0;
	public const int InvalidArguments = 2;
	public const int TargetNotFound = 3;
	public const int UnsupportedTarget = 4;
	public const int MissingArchitectureLauncher = 5;
	public const int MissingPayload = 6;
	public const int NativeInjectionFailed = 7;
	public const int MissingInjectorDll = 8;
	public const int UnexpectedFailure = 1;
}
