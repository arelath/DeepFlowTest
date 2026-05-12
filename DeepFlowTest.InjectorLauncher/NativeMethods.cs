namespace DeepFlowTest.InjectorLauncher;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;

internal static class NativeMethods
{
	public const uint AttachParentProcess = 0xFFFFFFFF;

	[Flags]
	public enum ProcessAccessFlags : uint
	{
		CreateThread = 0x00000002,
		VirtualMemoryOperation = 0x00000008,
		VirtualMemoryRead = 0x00000010,
		VirtualMemoryWrite = 0x00000020,
		QueryInformation = 0x00000400,
		QueryLimitedInformation = 0x00001000,
		Synchronize = 0x00100000,
		Injection =
			CreateThread |
			VirtualMemoryOperation |
			VirtualMemoryWrite |
			QueryInformation,
	}

	[Flags]
	public enum SnapshotFlags : uint
	{
		Module = 0x00000008,
		Module32 = 0x00000010,
	}

	public enum ImageFileMachine : ushort
	{
		Unknown = 0,
		I386 = 0x14C,
		Arm = 0x01C0,
		Amd64 = 0x8664,
		Arm64 = 0xAA64,
	}

	[Flags]
	public enum AllocationType
	{
		Commit = 0x1000,
		Reserve = 0x2000,
		Release = 0x8000,
	}

	[Flags]
	public enum MemoryProtection
	{
		ReadWrite = 0x04,
	}

	public enum WaitResult
	{
		WaitObject0 = 0x00,
		WaitTimeout = 0x102,
		WaitFailed = -1,
	}

	[DebuggerDisplay("{" + nameof(ModuleName) + "}")]
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	public struct ModuleEntry
	{
		public uint Size;
		public uint ModuleId;
		public uint ProcessId;
		public uint GlobalUsageCount;
		public uint ProcessUsageCount;
		public IntPtr BaseAddress;
		public uint BaseSize;
		public IntPtr ModuleHandle;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string ModuleName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string FilePath;
	}

	public sealed class ProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		private ProcessHandle()
			: base(true)
		{
		}

		protected override bool ReleaseHandle()
		{
			return CloseHandle(handle);
		}
	}

	private sealed class ToolHelpHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		private ToolHelpHandle()
			: base(true)
		{
		}

		protected override bool ReleaseHandle()
		{
			return CloseHandle(handle);
		}
	}

	public static IEnumerable<ModuleEntry> GetModules(Process process)
	{
		var module = default(ModuleEntry);
		var snapshot = CreateToolhelp32Snapshot(SnapshotFlags.Module | SnapshotFlags.Module32, process.Id);
		if (snapshot.IsInvalid)
			yield break;

		using (snapshot)
		{
			module.Size = (uint)Marshal.SizeOf(module);
			if (Module32First(snapshot, ref module))
			{
				do
				{
					yield return module;
				}
				while (Module32Next(snapshot, ref module));
			}
		}
	}

	public static ProcessHandle OpenProcess(Process process, ProcessAccessFlags flags)
	{
		return OpenProcess(flags, false, process.Id);
	}

	public static IntPtr GetRemoteProcAddress(Process targetProcess, string moduleName, string procName)
	{
		long functionOffsetFromBaseAddress = 0;

		foreach (ProcessModule? module in Process.GetCurrentProcess().Modules)
		{
			if (module?.ModuleName is null || module.FileName is null)
				continue;

			if (module.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase) ||
				module.FileName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
			{
				var procAddress = GetProcAddress(module.BaseAddress, procName).ToInt64();
				if (procAddress != 0)
					functionOffsetFromBaseAddress = procAddress - (long)module.BaseAddress;

				break;
			}
		}

		if (functionOffsetFromBaseAddress == 0)
			throw new InvalidOperationException($"Could not find local method handle for '{procName}' in module '{moduleName}'.");

		var remoteModuleHandle = GetRemoteModuleHandle(targetProcess, moduleName);
		return remoteModuleHandle == IntPtr.Zero ? IntPtr.Zero : new IntPtr((long)remoteModuleHandle + functionOffsetFromBaseAddress);
	}

	public static IntPtr GetRemoteModuleHandle(Process targetProcess, string moduleName)
	{
		foreach (ProcessModule? module in targetProcess.Modules)
		{
			if (module?.ModuleName is null || module.FileName is null)
				continue;

			if (module.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase) ||
				module.FileName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
			{
				return module.BaseAddress;
			}
		}

		return IntPtr.Zero;
	}

	public static string ToStableName(this ImageFileMachine machine)
	{
		return machine switch
		{
			ImageFileMachine.I386 => ArchitectureDetector.X86,
			ImageFileMachine.Amd64 => ArchitectureDetector.X64,
			ImageFileMachine.Arm => ArchitectureDetector.Arm,
			ImageFileMachine.Arm64 => ArchitectureDetector.Arm64,
			_ => throw new InjectorLauncherException(InjectorExitCode.UnsupportedTarget, $"Unsupported target machine type '{machine}'."),
		};
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool IsWow64Process2(IntPtr process, out ImageFileMachine processMachine, out ImageFileMachine nativeMachine);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool IsWow64Process(IntPtr process, out bool wow64Process);

	[DllImport("kernel32.dll", SetLastError = true)]
	[SuppressUnmanagedCodeSecurity]
	public static extern ProcessHandle OpenProcess(ProcessAccessFlags processAccess, bool inheritHandle, int processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool CloseHandle(IntPtr handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern ToolHelpHandle CreateToolhelp32Snapshot(SnapshotFlags flags, int processId);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
	private static extern bool Module32First(ToolHelpHandle snapshot, ref ModuleEntry module);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
	private static extern bool Module32Next(ToolHelpHandle snapshot, ref ModuleEntry module);

	[DllImport("user32.dll")]
	public static extern int GetWindowThreadProcessId(IntPtr hwnd, out int processId);

	[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
	public static extern IntPtr VirtualAllocEx(ProcessHandle process, IntPtr address, uint size, AllocationType allocationType, MemoryProtection protection);

	[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
	public static extern bool VirtualFreeEx(ProcessHandle process, IntPtr address, int size, AllocationType freeType);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool WriteProcessMemory(ProcessHandle process, IntPtr baseAddress, IntPtr buffer, uint size, out int bytesWritten);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
	public static extern IntPtr GetModuleHandle(string moduleName);

	[DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
	public static extern IntPtr GetProcAddress(IntPtr module, string procName);

	[DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
	public static extern IntPtr LoadLibrary(string libraryName);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool FreeLibrary(IntPtr module);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern IntPtr CreateRemoteThread(ProcessHandle process, IntPtr threadAttributes, uint stackSize, IntPtr startAddress, IntPtr parameter, uint creationFlags, out IntPtr threadId);

	[DllImport("kernel32.dll")]
	public static extern bool GetExitCodeThread(IntPtr thread, out IntPtr exitCode);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern WaitResult WaitForSingleObject(IntPtr handle, uint timeoutMilliseconds = 0xFFFFFFFF);

	[DllImport("kernel32", SetLastError = true)]
	public static extern bool AllocConsole();

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool AttachConsole(uint processId);
}
