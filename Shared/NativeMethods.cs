namespace DeepFlowTest.Shared;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static partial class NativeMethods
{
	public const uint AttachParentProcess = 0xFFFFFFFF;

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
}
