namespace DeepFlowTest.InjectorLauncher;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class ArchitectureDetector
{
	public const string X86 = "x86";
	public const string X64 = "x64";
	public const string Arm = "ARM";
	public const string Arm64 = "ARM64";

	public static string CurrentProcessArchitecture => Environment.Is64BitProcess ? X64 : X86;

	public static string Normalize(string architecture)
	{
		if (architecture.Equals(X86, StringComparison.OrdinalIgnoreCase) ||
			architecture.Equals("Win32", StringComparison.OrdinalIgnoreCase) ||
			architecture.Equals("I386", StringComparison.OrdinalIgnoreCase))
		{
			return X86;
		}

		if (architecture.Equals(X64, StringComparison.OrdinalIgnoreCase) ||
			architecture.Equals("AMD64", StringComparison.OrdinalIgnoreCase))
		{
			return X64;
		}

		if (architecture.Equals(Arm, StringComparison.OrdinalIgnoreCase))
			return Arm;

		if (architecture.Equals(Arm64, StringComparison.OrdinalIgnoreCase))
			return Arm64;

		throw new InjectorLauncherException(InjectorExitCode.UnsupportedTarget, $"Unsupported target architecture '{architecture}'.");
	}

	public static bool IsSupported(string architecture)
	{
		var normalized = Normalize(architecture);
		return normalized is X86 or X64;
	}

	public static string GetArchitecture(Process process)
	{
		try
		{
			using var processHandle = NativeMethods.OpenProcess(process, NativeMethods.ProcessAccessFlags.QueryLimitedInformation);
			if (processHandle.IsInvalid)
				throw new InvalidOperationException("Could not query process information.");

			try
			{
				if (!NativeMethods.IsWow64Process2(processHandle.DangerousGetHandle(), out var processMachine, out var nativeMachine))
					throw new System.ComponentModel.Win32Exception();

				var architecture = processMachine == NativeMethods.ImageFileMachine.Unknown ? nativeMachine : processMachine;
				return Normalize(architecture.ToStableName());
			}
			catch (EntryPointNotFoundException)
			{
				if (!NativeMethods.IsWow64Process(processHandle.DangerousGetHandle(), out var isWow64))
					throw new System.ComponentModel.Win32Exception();

				return isWow64 && Environment.Is64BitOperatingSystem ? X86 : CurrentProcessArchitecture;
			}
		}
		catch (InjectorLauncherException)
		{
			throw;
		}
		catch (Exception ex)
		{
			InjectorLog.Write($"Architecture detection failed for PID {process.Id}: {ex}");
			return CurrentProcessArchitecture;
		}
	}
}
