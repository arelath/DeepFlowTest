namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using DeepFlowTest;
using DeepFlowTest.Shared;

public sealed record class ProcessSnapshot
{
	public int ProcessId { get; set; }

	public string ProcessName { get; set; } = string.Empty;

	public string? MainWindowTitle { get; set; }

	public IReadOnlyList<ProcessWindowSnapshot> TopLevelWindows { get; set; } = [];

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
	public IReadOnlyList<ProcessSnapshot> Processes { get; set; } = [];

	public IReadOnlyList<ProcessInspectionWarning> Warnings { get; set; } = [];
}

public interface IProcessSnapshotSource
{
	ProcessSnapshotResult GetSnapshots();
}

public sealed class LiveProcessSnapshotSource : IProcessSnapshotSource
{
	public ProcessSnapshotResult GetSnapshots()
	{
		List<ProcessSnapshot> snapshots = [];
		List<ProcessInspectionWarning> warnings = [];
		var windowsByProcessId = EnumerateTopLevelWindowsByProcessId();
		foreach (var process in Process.GetProcesses())
		{
			try
			{
				snapshots.Add(CreateSnapshot(process, windowsByProcessId, warnings));
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

	private static ProcessSnapshot CreateSnapshot(
		Process process,
		IReadOnlyDictionary<int, IReadOnlyList<ProcessWindowSnapshot>> windowsByProcessId,
		List<ProcessInspectionWarning> warnings)
	{
		var processId = SafeGet(() => process.Id, 0);
		var processName = SafeGet(() => process.ProcessName, string.Empty);
		var hasExited = SafeGet(() => process.HasExited, true);
		var windows = processId > 0 && windowsByProcessId.TryGetValue(processId, out var processWindows)
			? processWindows
			: Array.Empty<ProcessWindowSnapshot>();
		var hasVisibleTopLevelWindow = HasVisibleTopLevelWindow(windows);
		var modules = hasVisibleTopLevelWindow
			? TryGetModuleNames(process, processId, processName, warnings)
			: Array.Empty<string>();
		var title = windows.FirstOrDefault(window => !string.IsNullOrWhiteSpace(window.Title))?.Title ?? string.Empty;
		if (string.IsNullOrWhiteSpace(title))
			title = SafeGet(() => process.MainWindowTitle, string.Empty);
		var framework = DetectFramework(modules);
		var shouldInspectArchitecture = hasVisibleTopLevelWindow || processId == Environment.ProcessId;

		return new ProcessSnapshot
		{
			ProcessId = processId,
			ProcessName = processName,
			MainWindowTitle = title,
			TopLevelWindows = windows,
			Architecture = shouldInspectArchitecture ? DetectArchitecture(process) : string.Empty,
			FrameworkFamily = framework,
			IsLikelyWpfCandidate = IsSupportedUiFramework(modules),
			HasExited = hasExited,
		};
	}

	private static IReadOnlyList<string> TryGetModuleNames(Process process, int processId, string processName, List<ProcessInspectionWarning> warnings)
	{
		try
		{
			return NativeMethods.GetModules(process)
				.Select(static module => module.ModuleName)
				.Where(static moduleName => !string.IsNullOrWhiteSpace(moduleName))
				.ToArray();
		}
		catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			warnings.Add(new ProcessInspectionWarning
			{
				ProcessId = processId == 0 ? null : processId,
				ProcessName = processName,
				Message = ex.Message,
			});
			return [];
		}
	}

	private static string DetectFramework(IReadOnlyList<string> modules)
	{
		if (modules.Any(IsWpfModule))
			return "wpf";

		if (modules.Any(IsWinFormsModule))
			return "winforms";

		if (modules.Any(static x => x.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase)))
			return "dotnet";

		if (modules.Any(static x => x.Equals("clr.dll", StringComparison.OrdinalIgnoreCase)))
			return "netframework";

		return string.Empty;
	}

	private static bool IsSupportedUiFramework(IReadOnlyList<string> modules) =>
		modules.Any(IsWpfModule) || modules.Any(IsWinFormsModule);

	private static bool IsWpfModule(string moduleName) =>
		IsModule(moduleName, "PresentationFramework")
		|| IsModule(moduleName, "PresentationCore");

	private static bool IsWinFormsModule(string moduleName) =>
		IsModule(moduleName, "System.Windows.Forms");

	private static bool IsModule(string moduleName, string baseName) =>
		moduleName.Equals(baseName + ".dll", StringComparison.OrdinalIgnoreCase)
		|| moduleName.Equals(baseName + ".ni.dll", StringComparison.OrdinalIgnoreCase);

	private static string DetectArchitecture(Process process)
	{
		if (!Environment.Is64BitOperatingSystem)
			return "x86";

		try
		{
			if (NativeMethods.IsWow64Process2(process.Handle, out var processMachine, out var nativeMachine))
			{
				return processMachine switch
				{
					NativeMethods.ImageFileMachine.I386 => "x86",
					NativeMethods.ImageFileMachine.Arm or NativeMethods.ImageFileMachine.ArmNt => "arm",
					NativeMethods.ImageFileMachine.Unknown when nativeMachine == NativeMethods.ImageFileMachine.Arm64 => "arm64",
					NativeMethods.ImageFileMachine.Unknown => "x64",
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
			return Environment.Is64BitProcess && NativeMethods.IsWow64Process(process.Handle, out var isWow64) && isWow64
				? "x86"
				: "x64";
		}
		catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			return "unknown";
		}
	}

	private static IReadOnlyDictionary<int, IReadOnlyList<ProcessWindowSnapshot>> EnumerateTopLevelWindowsByProcessId()
	{
		Dictionary<int, List<ProcessWindowSnapshot>> windowsByProcessId = [];
		NativeMethods.EnumWindows((hwnd, _) =>
		{
			NativeMethods.GetWindowThreadProcessId(hwnd, out var windowProcessId);
			if (windowProcessId <= 0 || !NativeMethods.IsWindowVisible(hwnd))
				return true;

			var titleLength = NativeMethods.GetWindowTextLength(hwnd);
			if (titleLength <= 0)
				return true;

			var builder = new StringBuilder(titleLength + 1);
			NativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
			if (!windowsByProcessId.TryGetValue(windowProcessId, out var windows))
			{
				windows = [];
				windowsByProcessId[windowProcessId] = windows;
			}

			windows.Add(new ProcessWindowSnapshot
			{
				Hwnd = hwnd.ToInt64(),
				Title = builder.ToString(),
			});
			return true;
		}, IntPtr.Zero);
		return windowsByProcessId.ToDictionary(
			static pair => pair.Key,
			static pair => (IReadOnlyList<ProcessWindowSnapshot>)pair.Value.ToArray());
	}

	private static bool HasVisibleTopLevelWindow(IReadOnlyList<ProcessWindowSnapshot> windows) =>
		windows.Any(static window => !string.IsNullOrWhiteSpace(window.Title));

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

}

public sealed class ProcessListData
{
	public IReadOnlyList<ProcessSnapshot> Processes { get; set; } = [];

	public IReadOnlyList<ProcessInspectionWarning> Warnings { get; set; } = [];

	public static ProcessListData FromSnapshotResult(
		ProcessSnapshotResult result,
		bool candidatesOnly,
		bool excludeExited = false,
		bool sortByProcessName = false)
	{
		_ = result ?? throw new ArgumentNullException(nameof(result));
		var warnedProcessIds = candidatesOnly
			? result.Warnings
				.Where(static warning => warning.ProcessId.HasValue)
				.Select(static warning => warning.ProcessId!.Value)
				.ToHashSet()
			: [];

		var processes = result.Processes
			.Where(process => !excludeExited || !process.HasExited)
			.Where(process => !candidatesOnly || IsAttachCandidate(process, warnedProcessIds));
		if (sortByProcessName)
		{
			processes = processes
				.OrderBy(static process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
				.ThenBy(static process => process.ProcessId);
		}

		var processArray = processes.ToArray();
		var warnings = candidatesOnly
			? FilterWarningsForProcesses(result.Warnings, processArray)
			: result.Warnings;
		return new ProcessListData
		{
			Processes = processArray,
			Warnings = warnings,
		};
	}

	private static bool IsAttachCandidate(ProcessSnapshot process, HashSet<int> warnedProcessIds) =>
		process.IsLikelyWpfCandidate
		&& !process.HasExited
		&& HasVisibleTopLevelWindow(process)
		&& !warnedProcessIds.Contains(process.ProcessId);

	private static bool HasVisibleTopLevelWindow(ProcessSnapshot process) =>
		process.TopLevelWindows.Any(static window => !string.IsNullOrWhiteSpace(window.Title));

	private static IReadOnlyList<ProcessInspectionWarning> FilterWarningsForProcesses(
		IReadOnlyList<ProcessInspectionWarning> warnings,
		IReadOnlyList<ProcessSnapshot> processes)
	{
		var processIds = processes.Select(static process => process.ProcessId).ToHashSet();
		return warnings
			.Where(warning => warning.ProcessId.HasValue && processIds.Contains(warning.ProcessId.Value))
			.ToArray();
	}
}
