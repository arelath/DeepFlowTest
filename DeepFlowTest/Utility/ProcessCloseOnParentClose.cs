namespace DeepFlowTest.Utility;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DeepFlowTest.Shared;

internal static class ProcessCloseOnParentClose
{
	private static readonly IntPtr jobHandle;

	static ProcessCloseOnParentClose()
	{
		if (Environment.OSVersion.Version < new Version(6, 2))
			return;

		var jobName = nameof(ProcessCloseOnParentClose) + Process.GetCurrentProcess().Id;
		jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, jobName);

		var info = new NativeMethods.JobObjectBasicLimitInformation
		{
			LimitFlags = NativeMethods.JobObjectLimit.KillOnJobClose,
		};
		var extendedInfo = new NativeMethods.JobObjectExtendedLimitInformation
		{
			BasicLimitInformation = info,
		};

		var length = Marshal.SizeOf(typeof(NativeMethods.JobObjectExtendedLimitInformation));
		var extendedInfoPtr = Marshal.AllocHGlobal(length);
		try
		{
			Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
			if (!NativeMethods.SetInformationJobObject(jobHandle, NativeMethods.JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
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

		if (!NativeMethods.AssignProcessToJobObject(jobHandle, process.Handle) && !process.HasExited)
			throw new Win32Exception();
	}
}
