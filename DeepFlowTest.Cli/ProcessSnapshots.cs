namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using DeepFlowTest;

public sealed class ProcessSnapshot
{
	public int ProcessId { get; set; }

	public string ProcessName { get; set; } = string.Empty;

	public string? MainWindowTitle { get; set; }

	public string? Architecture { get; set; }

	public string? FrameworkFamily { get; set; }

	public bool IsLikelyWpfCandidate { get; set; }

	public bool HasExited { get; set; }

	[JsonIgnore]
	public ITargetProcess? TargetProcess { get; set; }
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
		var title = SafeGet(() => process.MainWindowTitle, string.Empty);
		var hasExited = SafeGet(() => process.HasExited, true);
		var modules = TryGetModuleNames(process, processId, processName, warnings);
		var framework = DetectFramework(modules);

		return new ProcessSnapshot
		{
			ProcessId = processId,
			ProcessName = processName,
			MainWindowTitle = title,
			Architecture = Environment.Is64BitOperatingSystem ? "unknown" : "x86",
			FrameworkFamily = framework,
			IsLikelyWpfCandidate = modules.Any(static x => x.Equals("PresentationFramework.dll", StringComparison.OrdinalIgnoreCase))
				|| modules.Any(static x => x.Equals("PresentationCore.dll", StringComparison.OrdinalIgnoreCase)),
			HasExited = hasExited,
			TargetProcess = new TargetProcess(process),
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
	public IReadOnlyList<ProcessSnapshot> Processes { get; set; } = Array.Empty<ProcessSnapshot>();

	public IReadOnlyList<ProcessInspectionWarning> Warnings { get; set; } = Array.Empty<ProcessInspectionWarning>();
}
