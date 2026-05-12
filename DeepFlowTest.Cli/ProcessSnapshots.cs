namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using DeepFlowTest;

public sealed class ProcessSnapshot
{
	public int ProcessId { get; set; }

	public string ProcessName { get; set; } = string.Empty;

	public string? MainWindowTitle { get; set; }

	public IReadOnlyList<ProcessWindowSnapshot> TopLevelWindows { get; set; } = Array.Empty<ProcessWindowSnapshot>();

	public string? Architecture { get; set; }

	public string? FrameworkFamily { get; set; }

	public bool IsLikelyWpfCandidate { get; set; }

	public bool HasExited { get; set; }

	[JsonIgnore]
	public ITargetProcess? TargetProcess { get; set; }
}

public sealed class ProcessWindowSnapshot
{
	public long Hwnd { get; set; }

	public string Title { get; set; } = string.Empty;
}

public sealed class ProcessInspectionWarning
{
	public int? ProcessId { get; set; }

	public string ProcessName { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;
}

public sealed class ProcessSnapshotResult
{
	public IReadOnlyList<ProcessSnapshot> Processes { get; set; } = Array.Empty<ProcessSnapshot>();

	public IReadOnlyList<ProcessInspectionWarning> Warnings { get; set; } = Array.Empty<ProcessInspectionWarning>();
}

public interface IProcessSnapshotSource
{
	ProcessSnapshotResult GetSnapshots();
}

public sealed class LiveProcessSnapshotSource : IProcessSnapshotSource
{
	public ProcessSnapshotResult GetSnapshots()
	{
		var snapshots = new List<ProcessSnapshot>();
		var warnings = new List<ProcessInspectionWarning>();
		foreach (var process in Process.GetProcesses())
		{
			try
			{
				snapshots.Add(CreateSnapshot(process, warnings));
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
			{
				warnings.Add(new ProcessInspectionWarning
				{
					Message = ex.Message,
				});
			}
			finally
			{
				process.Dispose();
			}
		}

		return new ProcessSnapshotResult
		{
			Processes = snapshots,
			Warnings = warnings,
		};
	}

	private static ProcessSnapshot CreateSnapshot(Process process, List<ProcessInspectionWarning> warnings)
	{
		var processId = SafeGet(() => process.Id, 0);
		var processName = SafeGet(() => process.ProcessName, string.Empty);
		var hasExited = SafeGet(() => process.HasExited, true);
		var modules = TryGetModuleNames(process, processId, processName, warnings);
		var windows = EnumerateTopLevelWindows(processId);
		var title = SafeGet(() => process.MainWindowTitle, string.Empty);
		if (string.IsNullOrWhiteSpace(title))
			title = windows.FirstOrDefault(window => !string.IsNullOrWhiteSpace(window.Title))?.Title ?? string.Empty;
		var framework = DetectFramework(modules);
		var hasWindowCandidate = windows.Any(static window => !string.IsNullOrWhiteSpace(window.Title));

		return new ProcessSnapshot
		{
			ProcessId = processId,
			ProcessName = processName,
			MainWindowTitle = title,
			TopLevelWindows = windows,
			Architecture = DetectArchitecture(process),
			FrameworkFamily = framework,
			IsLikelyWpfCandidate = modules.Any(static x => x.Equals("PresentationFramework.dll", StringComparison.OrdinalIgnoreCase))
				|| modules.Any(static x => x.Equals("PresentationCore.dll", StringComparison.OrdinalIgnoreCase))
				|| hasWindowCandidate,
			HasExited = hasExited,
		};
	}

	private static IReadOnlyList<string> TryGetModuleNames(Process process, int processId, string processName, List<ProcessInspectionWarning> warnings)
	{
		try
		{
			return process.Modules.Cast<ProcessModule>().Select(static x => x.ModuleName).ToArray();
		}
		catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			warnings.Add(new ProcessInspectionWarning
			{
				ProcessId = processId == 0 ? null : processId,
				ProcessName = processName,
				Message = ex.Message,
			});
			return Array.Empty<string>();
		}
	}

	private static string DetectFramework(IReadOnlyList<string> modules)
	{
		if (modules.Any(static x => x.Equals("PresentationFramework.dll", StringComparison.OrdinalIgnoreCase)))
			return "wpf";

		if (modules.Any(static x => x.Equals("System.Windows.Forms.dll", StringComparison.OrdinalIgnoreCase)))
			return "winforms";

		if (modules.Any(static x => x.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase)))
			return "dotnet";

		if (modules.Any(static x => x.Equals("clr.dll", StringComparison.OrdinalIgnoreCase)))
			return "netframework";

		return string.Empty;
	}

	private static string DetectArchitecture(Process process)
	{
		if (!Environment.Is64BitOperatingSystem)
			return "x86";

		try
		{
			if (IsWow64Process2(process.Handle, out var processMachine, out var nativeMachine))
			{
				return processMachine switch
				{
					ImageFileMachineI386 => "x86",
					ImageFileMachineArm32 => "arm",
					0 when nativeMachine == ImageFileMachineArm64 => "arm64",
					0 => "x64",
					_ => "unknown",
				};
			}
		}
		catch (EntryPointNotFoundException)
		{
		}
		catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			return "unknown";
		}

		try
		{
			return Environment.Is64BitProcess && IsWow64Process(process.Handle, out var isWow64) && isWow64
				? "x86"
				: "x64";
		}
		catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			return "unknown";
		}
	}

	private static IReadOnlyList<ProcessWindowSnapshot> EnumerateTopLevelWindows(int processId)
	{
		if (processId <= 0)
			return Array.Empty<ProcessWindowSnapshot>();

		var windows = new List<ProcessWindowSnapshot>();
		EnumWindows((hwnd, _) =>
		{
			GetWindowThreadProcessId(hwnd, out var windowProcessId);
			if (windowProcessId != processId || !IsWindowVisible(hwnd))
				return true;

			var titleLength = GetWindowTextLength(hwnd);
			if (titleLength <= 0)
				return true;

			var builder = new StringBuilder(titleLength + 1);
			GetWindowText(hwnd, builder, builder.Capacity);
			windows.Add(new ProcessWindowSnapshot
			{
				Hwnd = hwnd.ToInt64(),
				Title = builder.ToString(),
			});
			return true;
		}, IntPtr.Zero);
		return windows;
	}

	private static T SafeGet<T>(Func<T> getter, T fallback)
	{
		try
		{
			return getter();
		}
		catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			return fallback;
		}
	}

	private const ushort ImageFileMachineI386 = 0x014c;
	private const ushort ImageFileMachineArm32 = 0x01c4;
	private const ushort ImageFileMachineArm64 = 0xaa64;

	private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hwnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowTextLength(IntPtr hwnd);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool IsWow64Process(IntPtr process, out bool wow64Process);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
}

public sealed class ProcessListData
{
	public IReadOnlyList<ProcessSnapshot> Processes { get; set; } = Array.Empty<ProcessSnapshot>();

	public IReadOnlyList<ProcessInspectionWarning> Warnings { get; set; } = Array.Empty<ProcessInspectionWarning>();
}
