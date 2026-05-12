namespace DeepFlowTest.InjectorLauncher;

using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using DeepFlowTest.Shared;

internal static class Injector
{
	public const string ExportName = "ExecuteInDefaultAppDomain";
	private const string ParameterDelimiter = "<|>";

	public static void InjectIntoProcess(ProcessWrapper processWrapper, InjectorData injectorData)
	{
		var paths = InjectorPathResolver.GetDllPaths(processWrapper.Architecture, AppContext.BaseDirectory, processWrapper.SupportedFrameworkFamily);
		InjectIntoProcess(processWrapper, injectorData, paths);
	}

	public static InjectorInvocation BuildInvocation(ProcessWrapper processWrapper, InjectorData injectorData, InjectorDllPaths paths, string nativeLogPath)
	{
		return BuildInvocation(processWrapper.SupportedFrameworkFamily, injectorData, paths, nativeLogPath);
	}

	public static InjectorInvocation BuildInvocation(string frameworkFamily, InjectorData injectorData, InjectorDllPaths paths, string nativeLogPath)
	{
		var parameters = new[]
		{
			frameworkFamily,
			injectorData.FullAssemblyPath,
			injectorData.ClassName,
			injectorData.MethodName,
			injectorData.StartupArgument,
			nativeLogPath,
		};

		if (parameters.Any(static part => part.Contains(ParameterDelimiter)))
			throw new InjectorLauncherException(InjectorExitCode.InvalidArguments, "Injector parameter values cannot contain the native delimiter.");

		return new InjectorInvocation(paths.InjectorDllName, paths.InjectorDllPath, string.Join(ParameterDelimiter, parameters));
	}

	private static void InjectIntoProcess(ProcessWrapper processWrapper, InjectorData injectorData, InjectorDllPaths paths)
	{
		if (!File.Exists(injectorData.FullAssemblyPath))
			throw new FileNotFoundException("Could not find payload assembly.", injectorData.FullAssemblyPath);

		if (!File.Exists(paths.InjectorDllPath))
			throw new InjectorLauncherException(InjectorExitCode.MissingInjectorDll, $"Could not find injector DLL '{paths.InjectorDllPath}'.");

		var nativeLogPath = InjectorLog.CreateNativeLogPath(processWrapper.Id);
		var invocation = BuildInvocation(processWrapper, injectorData, paths, nativeLogPath);
		InjectorLog.Write($"Injecting PID={processWrapper.Id}, Architecture={processWrapper.Architecture}, Framework={processWrapper.SupportedFrameworkFamily}, Payload={injectorData.FullAssemblyPath}, NativeDll={paths.InjectorDllPath}");

		var remoteAddress = IntPtr.Zero;
		var address = IntPtr.Zero;
		var hLibrary = IntPtr.Zero;
		var remoteThread = IntPtr.Zero;
		var moduleHandleInForeignProcess = IntPtr.Zero;
		var bufLen = (invocation.NativeParameter.Length + 1) * Marshal.SizeOf(typeof(char));

		try
		{
			remoteAddress = NativeMethods.VirtualAllocEx(
				processWrapper.Handle,
				IntPtr.Zero,
				(uint)bufLen,
				NativeMethods.AllocationType.Commit | NativeMethods.AllocationType.Reserve,
				NativeMethods.MemoryProtection.ReadWrite);

			if (remoteAddress == IntPtr.Zero)
				throw new Win32Exception("Failed to allocate memory in the target process.");

			address = Marshal.StringToHGlobalUni(invocation.NativeParameter);
			var size = (uint)(sizeof(char) * invocation.NativeParameter.Length);
			if (!NativeMethods.WriteProcessMemory(processWrapper.Handle, remoteAddress, address, size, out var bytesWritten) || bytesWritten == 0)
				throw Marshal.GetExceptionForHR(Marshal.GetLastWin32Error()) ?? new InvalidOperationException("Unknown error while writing to target process memory.");

			hLibrary = NativeMethods.LoadLibrary(paths.InjectorDllPath);
			if (hLibrary == IntPtr.Zero)
				throw new Win32Exception("Failed to load native injector into launcher process.");

			moduleHandleInForeignProcess = LoadLibraryInForeignProcess(processWrapper, paths.InjectorDllPath);
			var remoteProcAddress = NativeMethods.GetRemoteProcAddress(processWrapper.Process, paths.InjectorDllName, ExportName);
			if (remoteProcAddress == IntPtr.Zero)
				throw new InjectorLauncherException(InjectorExitCode.NativeInjectionFailed, $"Could not find native export '{ExportName}'.");

			remoteThread = NativeMethods.CreateRemoteThread(processWrapper.Handle, IntPtr.Zero, 0, remoteProcAddress, remoteAddress, 0, out _);
			if (remoteThread == IntPtr.Zero)
				throw new Win32Exception("Failed to create remote thread.");

			NativeMethods.WaitForSingleObject(remoteThread);
			_ = NativeMethods.GetExitCodeThread(remoteThread, out var nativeResult);
			InjectorLog.Write($"Native injector HRESULT={nativeResult}.");

			if (File.Exists(nativeLogPath))
				InjectorLog.Write(File.ReadAllText(nativeLogPath));

			if (nativeResult != IntPtr.Zero && PayloadLogLocator.TryReadTail(injectorData.StartupArgument, processWrapper.Id, out var payloadLogTail))
				InjectorLog.Write($"Payload log tail:{Environment.NewLine}{payloadLogTail}");

			if (nativeResult != IntPtr.Zero)
			{
				var nativeException = Marshal.GetExceptionForHR((int)nativeResult.ToInt64());
				throw nativeException is null
					? new InjectorLauncherException(InjectorExitCode.NativeInjectionFailed, $"Native injector failed with HRESULT {nativeResult}.")
					: new InjectorLauncherException(InjectorExitCode.NativeInjectionFailed, $"Native injector failed with HRESULT {nativeResult}: {nativeException.Message}", nativeException);
			}
		}
		finally
		{
			if (remoteThread != IntPtr.Zero)
				NativeMethods.CloseHandle(remoteThread);
			if (moduleHandleInForeignProcess != IntPtr.Zero)
				FreeLibraryInForeignProcess(processWrapper, paths.InjectorDllName, moduleHandleInForeignProcess);
			if (hLibrary != IntPtr.Zero)
				NativeMethods.FreeLibrary(hLibrary);
			if (remoteAddress != IntPtr.Zero)
				NativeMethods.VirtualFreeEx(processWrapper.Handle, remoteAddress, 0, NativeMethods.AllocationType.Release);
			if (address != IntPtr.Zero)
				Marshal.FreeHGlobal(address);
		}
	}

	private static IntPtr LoadLibraryInForeignProcess(ProcessWrapper processWrapper, string pathToDll)
	{
		var remoteAddress = IntPtr.Zero;
		var address = IntPtr.Zero;
		var remoteThread = IntPtr.Zero;
		var bufLen = (pathToDll.Length + 1) * Marshal.SizeOf(typeof(char));

		try
		{
			remoteAddress = NativeMethods.VirtualAllocEx(processWrapper.Handle, IntPtr.Zero, (uint)bufLen, NativeMethods.AllocationType.Commit, NativeMethods.MemoryProtection.ReadWrite);
			if (remoteAddress == IntPtr.Zero)
				throw new Win32Exception("Failed to allocate memory for LoadLibrary path.");

			address = Marshal.StringToHGlobalUni(pathToDll);
			if (!NativeMethods.WriteProcessMemory(processWrapper.Handle, remoteAddress, address, (uint)(sizeof(char) * pathToDll.Length), out var bytesWritten) || bytesWritten == 0)
				throw Marshal.GetExceptionForHR(Marshal.GetLastWin32Error()) ?? new InvalidOperationException("Unknown error while writing LoadLibrary path.");

			var kernel32 = NativeMethods.GetModuleHandle("kernel32");
			var loadLibraryW = NativeMethods.GetProcAddress(kernel32, "LoadLibraryW");
			if (loadLibraryW == IntPtr.Zero)
				throw new Win32Exception("Failed to get LoadLibraryW.");

			remoteThread = NativeMethods.CreateRemoteThread(processWrapper.Handle, IntPtr.Zero, 0, loadLibraryW, remoteAddress, 0, out _);
			if (remoteThread == IntPtr.Zero)
				throw new Win32Exception("Failed to create LoadLibrary remote thread.");

			NativeMethods.WaitForSingleObject(remoteThread);
			if (!NativeMethods.GetExitCodeThread(remoteThread, out var moduleHandle) || moduleHandle == IntPtr.Zero)
				throw new Win32Exception("Failed to load native injector in the target process.");

			return NativeMethods.GetRemoteModuleHandle(processWrapper.Process, Path.GetFileName(pathToDll));
		}
		finally
		{
			if (remoteThread != IntPtr.Zero)
				NativeMethods.CloseHandle(remoteThread);
			if (remoteAddress != IntPtr.Zero)
				NativeMethods.VirtualFreeEx(processWrapper.Handle, remoteAddress, 0, NativeMethods.AllocationType.Release);
			if (address != IntPtr.Zero)
				Marshal.FreeHGlobal(address);
		}
	}

	private static bool FreeLibraryInForeignProcess(ProcessWrapper processWrapper, string moduleName, IntPtr moduleHandleInForeignProcess)
	{
		var freeLibraryAndExitThread = NativeMethods.GetProcAddress(NativeMethods.GetModuleHandle("kernel32"), "FreeLibraryAndExitThread");
		if (freeLibraryAndExitThread == IntPtr.Zero)
			return false;

		var remoteThread = NativeMethods.CreateRemoteThread(processWrapper.Handle, IntPtr.Zero, 0, freeLibraryAndExitThread, moduleHandleInForeignProcess, 0, out _);
		try
		{
			if (remoteThread == IntPtr.Zero)
				return false;

			NativeMethods.WaitForSingleObject(remoteThread);
			return true;
		}
		finally
		{
			if (remoteThread != IntPtr.Zero)
				NativeMethods.CloseHandle(remoteThread);
		}
	}
}

internal sealed class InjectorInvocation
{
	public InjectorInvocation(string injectorDllName, string injectorDllPath, string nativeParameter)
	{
		InjectorDllName = injectorDllName;
		InjectorDllPath = injectorDllPath;
		NativeParameter = nativeParameter;
	}

	public string InjectorDllName { get; }

	public string InjectorDllPath { get; }

	public string NativeParameter { get; }
}
