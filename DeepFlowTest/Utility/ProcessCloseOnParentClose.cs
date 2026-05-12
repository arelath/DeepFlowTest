namespace DeepFlowTest.Utility;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class ProcessCloseOnParentClose
{
	private static readonly IntPtr jobHandle;

	static ProcessCloseOnParentClose()
	{
		if (Environment.OSVersion.Version < new Version(6, 2))
			return;

		var jobName = nameof(ProcessCloseOnParentClose) + Process.GetCurrentProcess().Id;
		jobHandle = CreateJobObject(IntPtr.Zero, jobName);

		var info = new JobObjectBasicLimitInformation
		{
			LimitFlags = JobObjectLimit.KillOnJobClose,
		};
		var extendedInfo = new JobObjectExtendedLimitInformation
		{
			BasicLimitInformation = info,
		};

		var length = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation));
		var extendedInfoPtr = Marshal.AllocHGlobal(length);
		try
		{
			Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
			if (!SetInformationJobObject(jobHandle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
				throw new Win32Exception();
		}
		finally
		{
			Marshal.FreeHGlobal(extendedInfoPtr);
		}
	}

	public static void Add(Process process)
	{
		_ = process ?? throw new ArgumentNullException(nameof(process));

		if (jobHandle == IntPtr.Zero)
			return;

		if (!AssignProcessToJobObject(jobHandle, process.Handle) && !process.HasExited)
			throw new Win32Exception();
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string name);

	[DllImport("kernel32.dll")]
	private static extern bool SetInformationJobObject(IntPtr job, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

	private enum JobObjectInfoType
	{
		ExtendedLimitInformation = 9,
	}

	[Flags]
	private enum JobObjectLimit : uint
	{
		KillOnJobClose = 0x2000,
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectBasicLimitInformation
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
	private struct IoCounters
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectExtendedLimitInformation
	{
		public JobObjectBasicLimitInformation BasicLimitInformation;
		public IoCounters IoInfo;
		public UIntPtr ProcessMemoryLimit;
		public UIntPtr JobMemoryLimit;
		public UIntPtr PeakProcessMemoryUsed;
		public UIntPtr PeakJobMemoryUsed;
	}
}
