namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Diagnostics;

internal static class PayloadEnvironment
{
	public static int ProcessId => Process.GetCurrentProcess().Id;

	public static string ProcessArchitecture => Environment.Is64BitProcess ? "x64" : "x86";

	public static string FrameworkFamily
	{
		get
		{
#if NETFRAMEWORK
			return "netframework";
#else
			var major = Environment.Version.Major;
			return major <= 3 ? "netcoreapp" : "dotnet";
#endif
		}
	}
}
