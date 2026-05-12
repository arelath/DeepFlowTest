namespace DeepFlowTest.InjectorLauncher;

using System;
using System.Diagnostics;

internal sealed class ProcessWrapper : IDisposable
{
	private ProcessWrapper(Process process, IntPtr windowHandle)
	{
		Process = process ?? throw new ArgumentNullException(nameof(process));
		Id = process.Id;
		WindowHandle = windowHandle;
		Architecture = ArchitectureDetector.GetArchitecture(process);
		if (!ArchitectureDetector.IsSupported(Architecture))
			throw new InjectorLauncherException(InjectorExitCode.UnsupportedTarget, $"Architecture '{Architecture}' is not supported.");

		SupportedFrameworkFamily = FrameworkDetector.Classify(process);
		Handle = NativeMethods.OpenProcess(process, NativeMethods.ProcessAccessFlags.Injection);
		if (Handle.IsInvalid)
			throw new InjectorLauncherException(InjectorExitCode.TargetNotFound, $"Could not open target process {process.Id}.");
	}

	public Process Process { get; }

	public int Id { get; }

	public NativeMethods.ProcessHandle Handle { get; }

	public IntPtr WindowHandle { get; }

	public string Architecture { get; }

	public string SupportedFrameworkFamily { get; }

	public static ProcessWrapper? From(int processId, IntPtr windowHandle)
	{
		try
		{
			return new ProcessWrapper(Process.GetProcessById(processId), windowHandle);
		}
		catch (ArgumentException ex)
		{
			InjectorLog.Write($"Could not find process {processId}: {ex.Message}");
			return null;
		}
		catch (InvalidOperationException ex)
		{
			InjectorLog.Write($"Process {processId} exited during resolution: {ex.Message}");
			return null;
		}
	}

	public static ProcessWrapper? FromWindowHandle(IntPtr windowHandle)
	{
		_ = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
		return processId == 0 ? null : From(processId, windowHandle);
	}

	public void Dispose()
	{
		Handle.Dispose();
		Process.Dispose();
	}
}
