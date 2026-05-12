namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using DeepFlowTest;

public sealed class TargetSelector
{
	public int? ProcessId { get; set; }

	public string? ProcessName { get; set; }

	public string? WindowTitle { get; set; }
}

public sealed class TargetInfo
{
	public int ProcessId { get; set; }

	public string ProcessName { get; set; } = string.Empty;

	public string? MainWindowTitle { get; set; }

	public string? Architecture { get; set; }

	public string? FrameworkFamily { get; set; }

	public bool IsLikelyWpfCandidate { get; set; }

	[JsonIgnore]
	public ITargetProcess? TargetProcess { get; set; }

	public ITargetProcess OpenProcess()
	{
		if (TargetProcess is not null)
			return TargetProcess;

		try
		{
			return new TargetProcess(Process.GetProcessById(ProcessId));
		}
		catch (ArgumentException ex)
		{
			throw new CliException(CliErrorCodes.TargetExited, $"Target process {ProcessId} exited during resolution.", ex.Message);
		}
	}

	public static TargetInfo FromSnapshot(ProcessSnapshot snapshot) =>
		new()
		{
			ProcessId = snapshot.ProcessId,
			ProcessName = snapshot.ProcessName,
			MainWindowTitle = snapshot.MainWindowTitle,
			Architecture = snapshot.Architecture,
			FrameworkFamily = snapshot.FrameworkFamily,
			IsLikelyWpfCandidate = snapshot.IsLikelyWpfCandidate,
			TargetProcess = snapshot.TargetProcess,
		};
}

public sealed class ProcessNameCache
{
	private readonly Dictionary<string, ProcessSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);

	public void Remember(IEnumerable<ProcessSnapshot> processes)
	{
		foreach (var process in processes)
		{
			if (!process.HasExited && !string.IsNullOrWhiteSpace(process.ProcessName))
				snapshots[process.ProcessName] = process;
		}
	}

	public IReadOnlyList<ProcessSnapshot> Find(string processName)
	{
		if (snapshots.TryGetValue(processName, out var exact))
			return new[] { exact };

		return snapshots.Values
			.Where(process => process.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase))
			.ToArray();
	}
}

public interface ITargetResolver
{
	TargetInfo Resolve(TargetSelector selector);
}

public sealed class TargetResolver : ITargetResolver
{
	private readonly IProcessSnapshotSource snapshotSource;
	private readonly ProcessNameCache processNameCache;

	public TargetResolver(IProcessSnapshotSource snapshotSource, ProcessNameCache? processNameCache = null)
	{
		this.snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
		this.processNameCache = processNameCache ?? new ProcessNameCache();
	}

	public TargetInfo Resolve(TargetSelector selector)
	{
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		if (selector.ProcessId.HasValue)
			return ResolveByPid(selector.ProcessId.Value);

		if (!string.IsNullOrWhiteSpace(selector.ProcessName))
			return ResolveByName(selector.ProcessName);

		if (!string.IsNullOrWhiteSpace(selector.WindowTitle))
			return ResolveByWindowTitle(selector.WindowTitle);

		throw new CliException(CliErrorCodes.InvalidArguments, "A target selector is required.");
	}

	private TargetInfo ResolveByPid(int processId)
	{
		var result = snapshotSource.GetSnapshots();
		var match = result.Processes.FirstOrDefault(process => process.ProcessId == processId);
		if (match is null)
			throw new CliException(CliErrorCodes.TargetNotFound, $"Process {processId} was not found.");

		return ToTarget(match);
	}

	private TargetInfo ResolveByName(string processName)
	{
		var cached = processNameCache.Find(processName);
		if (cached.Count == 1 && !cached[0].HasExited)
			return ToTarget(cached[0]);

		var result = snapshotSource.GetSnapshots();
		processNameCache.Remember(result.Processes);
		var exact = result.Processes
			.Where(process => process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		if (exact.Length != 0)
			return ExactlyOne(exact, $"process name '{processName}'");

		var contains = result.Processes
			.Where(process => process.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		return ExactlyOne(contains, $"process name containing '{processName}'");
	}

	private TargetInfo ResolveByWindowTitle(string windowTitle)
	{
		var result = snapshotSource.GetSnapshots();
		var matches = result.Processes
			.Where(process => !string.IsNullOrWhiteSpace(process.MainWindowTitle)
				&& process.MainWindowTitle.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		return ExactlyOne(matches, $"window title containing '{windowTitle}'");
	}

	private TargetInfo ExactlyOne(IReadOnlyList<ProcessSnapshot> matches, string description)
	{
		if (matches.Count == 0)
			throw new CliException(CliErrorCodes.TargetNotFound, $"No target matched {description}.");

		if (matches.Count > 1)
		{
			var candidates = matches.Select(static process => new
			{
				process.ProcessId,
				process.ProcessName,
				process.MainWindowTitle,
			}).ToArray();
			throw new CliException(CliErrorCodes.AmbiguousTarget, $"Multiple targets matched {description}.", candidates);
		}

		return ToTarget(matches[0]);
	}

	private static TargetInfo ToTarget(ProcessSnapshot snapshot)
	{
		if (snapshot.HasExited)
			throw new CliException(CliErrorCodes.TargetExited, $"Target process {snapshot.ProcessId} exited during resolution.");

		return TargetInfo.FromSnapshot(snapshot);
	}
}
