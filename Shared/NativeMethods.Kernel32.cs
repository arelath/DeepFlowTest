namespace DeepFlowTest.Shared;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

internal static partial class NativeMethods
{
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
		ArmNt = 0x01C4,
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

	public enum JobObjectInfoType
	{
		ExtendedLimitInformation = 9,
	}

	[Flags]
	public enum JobObjectLimit : uint
	{
		KillOnJobClose = 0x2000,
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

	[StructLayout(LayoutKind.Sequential)]
	public struct JobObjectBasicLimitInformation
	{
		public long PerProcessUserTimeLimit;
		public long PerJobUserTimeLimit;
		public JobObjectLimit LimitFlags;
		public UIntPtr MinimumWorkingSetSize;
		public UIntPtr MaximumWorkingSetSize;
		public uint ActiveProcessLimit;
		public long Affinity;
		public uint PriorityClass;
		public uint SchedulingClass;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct IoCounters
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct JobObjectExtendedLimitInformation
	{
		public JobObjectBasicLimitInformation BasicLimitInformation;
		public IoCounters IoInfo;
		public UIntPtr ProcessMemoryLimit;
		public UIntPtr JobMemoryLimit;
		public UIntPtr PeakProcessMemoryUsed;
		public UIntPtr PeakJobMemoryUsed;
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

	[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
	public static extern IntPtr VirtualAllocEx(ProcessHandle process, IntPtr address, uint size, AllocationType allocationType, MemoryProtection protection);

	[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
	public static extern bool VirtualFreeEx(ProcessHandle process, IntPtr address, int size, AllocationType freeType);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool WriteProcessMemory(ProcessHandle process, IntPtr baseAddress, IntPtr buffer, uint size, out int bytesWritten);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
	public static extern IntPtr GetModuleHandle(string moduleName);

	[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
	public static extern IntPtr GetProcAddress(IntPtr module, string procName);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	public static extern IntPtr LoadLibrary(string libraryName);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool FreeLibrary(IntPtr module);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern IntPtr CreateRemoteThread(ProcessHandle process, IntPtr threadAttributes, uint stackSize, IntPtr startAddress, IntPtr parameter, uint creationFlags, out IntPtr threadId);

	[DllImport("kernel32.dll")]
	public static extern bool GetExitCodeThread(IntPtr thread, out IntPtr exitCode);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern WaitResult WaitForSingleObject(IntPtr handle, uint timeoutMilliseconds = 0xFFFFFFFF);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool AllocConsole();

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool AttachConsole(uint processId);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool SetInformationJobObject(IntPtr job, JobObjectInfoType infoType, IntPtr jobObjectInfo, uint jobObjectInfoLength);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
}
