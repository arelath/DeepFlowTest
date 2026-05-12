namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
	private readonly Dictionary<string, List<ProcessSnapshot>> snapshots = new(StringComparer.OrdinalIgnoreCase);

	public void Remember(IEnumerable<ProcessSnapshot> processes)
	{
		foreach (var process in processes)
		{
			if (process.HasExited || string.IsNullOrWhiteSpace(process.ProcessName))
				continue;

			if (!snapshots.TryGetValue(process.ProcessName, out var bucket))
			{
				bucket = new List<ProcessSnapshot>();
				snapshots[process.ProcessName] = bucket;
			}

			var existingIndex = bucket.FindIndex(item => item.ProcessId == process.ProcessId);
			if (existingIndex >= 0)
				bucket[existingIndex] = process;
			else
				bucket.Add(process);
		}
	}

	public IReadOnlyList<ProcessSnapshot> Find(string processName)
	{
		if (snapshots.TryGetValue(processName, out var exact))
			return exact.Where(static process => !process.HasExited).ToArray();

		return snapshots.Values.SelectMany(static process => process)
			.Where(process => process.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase))
			.Where(static process => !process.HasExited)
			.ToArray();
	}
}

public interface IProcessNameCache
{
	int? TryGet(string processName);

	void Set(string processName, int processId);

	void Remove(string processName);
}

public sealed class NullProcessNameCache : IProcessNameCache
{
	public static readonly NullProcessNameCache Instance = new();

	private NullProcessNameCache()
	{
	}

	public int? TryGet(string processName) => null;

	public void Set(string processName, int processId)
	{
	}

	public void Remove(string processName)
	{
	}
}

public sealed class FileProcessNameCache : IProcessNameCache
{
	public const string FileName = "DeepFlowTest.Cli.ProcessCache.json";

	private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
	};

	private readonly string path;

	public FileProcessNameCache(string? path = null)
	{
		this.path = path ?? DefaultPath;
	}

	public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, FileName);

	public int? TryGet(string processName)
	{
		var document = TryRead();
		return document.ProcessNames.TryGetValue(NormalizeKey(processName), out var processId)
			? processId
			: null;
	}

	public void Set(string processName, int processId)
	{
		var document = TryRead();
		document.ProcessNames[NormalizeKey(processName)] = processId;
		TryWrite(document);
	}

	public void Remove(string processName)
	{
		var document = TryRead();
		if (!document.ProcessNames.Remove(NormalizeKey(processName)))
			return;

		TryWrite(document);
	}

	private ProcessNameCacheDocument TryRead()
	{
		try
		{
			if (!File.Exists(path))
				return new ProcessNameCacheDocument();

			return JsonSerializer.Deserialize<ProcessNameCacheDocument>(File.ReadAllText(path, Utf8NoBom), JsonOptions)
				?? new ProcessNameCacheDocument();
		}
		catch (Exception ex) when (IsCacheIoException(ex))
		{
			return new ProcessNameCacheDocument();
		}
	}

	private void TryWrite(ProcessNameCacheDocument document)
	{
		try
		{
			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(directory))
				Directory.CreateDirectory(directory);

			File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions), Utf8NoBom);
		}
		catch (Exception ex) when (IsCacheIoException(ex))
		{
		}
	}

	private static string NormalizeKey(string processName) =>
		Path.GetFileNameWithoutExtension(processName ?? string.Empty).Trim().ToLowerInvariant();

	private static bool IsCacheIoException(Exception ex) =>
		ex is IOException
			or UnauthorizedAccessException
			or NotSupportedException
			or System.Security.SecurityException
			or JsonException;

	private sealed class ProcessNameCacheDocument
	{
		public int SchemaVersion { get; set; } = 1;

		public Dictionary<string, int> ProcessNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
	private readonly IProcessNameCache fileProcessNameCache;

	public TargetResolver(
		IProcessSnapshotSource snapshotSource,
		ProcessNameCache? processNameCache = null,
		IProcessNameCache? fileProcessNameCache = null)
	{
		this.snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
		this.processNameCache = processNameCache ?? new ProcessNameCache();
		this.fileProcessNameCache = fileProcessNameCache ?? new FileProcessNameCache();
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
		var normalizedProcessName = NormalizeProcessName(processName);
		var cached = processNameCache.Find(normalizedProcessName);
		if (cached.Count != 0)
			return ExactlyOne(cached, $"cached process name '{processName}'");

		var cachedFileTarget = TryResolveFileCachedProcessName(normalizedProcessName);
		if (cachedFileTarget is not null)
			return cachedFileTarget;

		var result = snapshotSource.GetSnapshots();
		processNameCache.Remember(result.Processes);
		var exact = result.Processes
			.Where(process => NormalizeProcessName(process.ProcessName).Equals(normalizedProcessName, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		if (exact.Length != 0)
			return CacheAndReturn(ExactlyOne(exact, $"process name '{processName}'"), normalizedProcessName);

		var contains = result.Processes
			.Where(process => NormalizeProcessName(process.ProcessName).Contains(normalizedProcessName, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		return CacheAndReturn(ExactlyOne(contains, $"process name containing '{processName}'"), normalizedProcessName);
	}

	private TargetInfo? TryResolveFileCachedProcessName(string normalizedProcessName)
	{
		var cachedProcessId = fileProcessNameCache.TryGet(normalizedProcessName);
		if (!cachedProcessId.HasValue)
			return null;

		var result = snapshotSource.GetSnapshots();
		var match = result.Processes.FirstOrDefault(process => process.ProcessId == cachedProcessId.Value);
		if (match is not null && ProcessNameMatches(match, normalizedProcessName))
			return ToTarget(match);

		fileProcessNameCache.Remove(normalizedProcessName);
		return null;
	}

	private TargetInfo CacheAndReturn(TargetInfo target, string normalizedProcessName)
	{
		fileProcessNameCache.Set(normalizedProcessName, target.ProcessId);
		return target;
	}

	private static bool ProcessNameMatches(ProcessSnapshot process, string normalizedProcessName)
	{
		var candidateName = NormalizeProcessName(process.ProcessName);
		return candidateName.Equals(normalizedProcessName, StringComparison.OrdinalIgnoreCase)
			|| candidateName.Contains(normalizedProcessName, StringComparison.OrdinalIgnoreCase);
	}

	private TargetInfo ResolveByWindowTitle(string windowTitle)
	{
		var result = snapshotSource.GetSnapshots();
		var matches = result.Processes
			.Where(process => WindowTitleMatches(process, windowTitle))
			.ToArray();
		return ExactlyOne(matches, $"window title containing '{windowTitle}'");
	}

	private static bool WindowTitleMatches(ProcessSnapshot process, string windowTitle)
	{
		if (!string.IsNullOrWhiteSpace(process.MainWindowTitle)
			&& process.MainWindowTitle.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return process.TopLevelWindows.Any(window => !string.IsNullOrWhiteSpace(window.Title)
			&& window.Title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase));
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

	private static string NormalizeProcessName(string processName) =>
		Path.GetFileNameWithoutExtension(processName ?? string.Empty).Trim();
}
