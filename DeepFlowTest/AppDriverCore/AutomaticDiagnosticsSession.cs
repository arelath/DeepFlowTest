namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DeepFlowTest.Assert.TestFrameworks;
using DeepFlowTest.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

internal sealed class AutomaticDiagnosticsSession
{
	private static readonly JsonSerializerSettings JsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		Formatting = Formatting.Indented,
		NullValueHandling = NullValueHandling.Ignore,
		TypeNameHandling = TypeNameHandling.None,
	};

	private readonly AppDriver driver;
	private readonly AutomaticDiagnosticsOptions options;
	private readonly AppDriverDiagnosticsCollector diagnostics;
	private readonly IDiagnosticsArtifactSink sink;
	private readonly DiagnosticsTestContext initialTestContext;
	private readonly string artifactRoot;
	private readonly string sessionDirectory;
	private readonly string? configuredTracePath;
	private readonly List<DiagnosticsArtifactManifestEntry> artifacts = [];
	private SemanticRecordingSession? recording;
	private Exception? observedFailure;
	private bool completed;

	private AutomaticDiagnosticsSession(
		AppDriver driver,
		AutomaticDiagnosticsOptions options,
		AppDriverDiagnosticsCollector diagnostics,
		string? configuredTracePath)
	{
		this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
		this.configuredTracePath = configuredTracePath;
		sink = options.ArtifactSink ?? new TestFrameworkArtifactSink();
		initialTestContext = GetTestContextSafely();
		artifactRoot = ResolveArtifactRoot(initialTestContext);
		sessionDirectory = configuredTracePath is null
			? Path.Combine(artifactRoot, CreateSessionDirectoryName(initialTestContext.TestName, driver.Connection.TargetProcess.Id))
			: Path.GetDirectoryName(Path.GetFullPath(configuredTracePath)) ?? artifactRoot;
	}

	public string? TracePath => recording?.OutputPath;

	public string? ManifestPath { get; private set; }

	public string? ArtifactDirectory => completed || options.Mode == AutomaticDiagnosticsMode.Always ? sessionDirectory : null;

	public static AutomaticDiagnosticsSession Create(
		AppDriver driver,
		AutomaticDiagnosticsOptions options,
		AppDriverDiagnosticsCollector diagnostics,
		string? configuredTracePath = null)
	{
		var session = new AutomaticDiagnosticsSession(driver, options, diagnostics, configuredTracePath);
		session.StartSafely();
		return session;
	}

	public void MarkFailure(Exception? failure)
	{
		Interlocked.CompareExchange(ref observedFailure, failure ?? new InvalidOperationException("The test was marked as failed."), null);
	}

	public void Complete()
	{
		if (completed)
			return;
		completed = true;
		try
		{
			CompleteCore();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			RecordDiagnostic(AppDriverDiagnosticSeverity.Error, "automatic-diagnostics-completion-failed", "Automatic diagnostics could not be completed.", ex);
		}
	}

	private void StartSafely()
	{
		if (options.Mode is AutomaticDiagnosticsMode.Off or AutomaticDiagnosticsMode.Manual)
			return;
		try
		{
			ApplyRetentionPolicy();
			var timeoutMs = DurationUtility.ToMilliseconds(driver.Options.Timeout, nameof(driver.Options.Timeout));
			var recordingOptions = CreateBoundedRecordingOptions();
			if (options.Mode == AutomaticDiagnosticsMode.Always)
			{
				var outputPath = configuredTracePath ?? Path.Combine(sessionDirectory, "semantic-trace" + SemanticRecordingFrameWriter.GetDefaultExtension(recordingOptions.OutputFormat));
				recording = SemanticRecordingSession.Start(driver.Session, outputPath, recordingOptions, timeoutMs);
			}
			else
			{
				recording = SemanticRecordingSession.StartBuffered(driver.Session, recordingOptions, options.FailureBufferSizeBytes, timeoutMs);
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			RecordDiagnostic(AppDriverDiagnosticSeverity.Warning, "automatic-recording-start-failed", "Automatic semantic recording could not be started; driver construction will continue.", ex);
		}
	}

	private SemanticRecordingOptions CreateBoundedRecordingOptions()
	{
		var source = options.Recording;
		var manifestReserve = Math.Min(64 * 1024, Math.Max(1024, options.MaximumArtifactSizeBytes / 10));
		var traceLimit = Math.Max(1, options.MaximumArtifactSizeBytes - manifestReserve);
		return new SemanticRecordingOptions
		{
			Interval = source.Interval,
			PropNames = source.PropNames,
			RootTargetId = source.RootTargetId,
			IncludeInitialSnapshot = source.IncludeInitialSnapshot,
			TextIdleDuration = source.TextIdleDuration,
			MaxQueuedActions = source.MaxQueuedActions,
			MaxBatchFrames = source.MaxBatchFrames,
			MaxNodeCount = source.MaxNodeCount,
			Timeout = source.Timeout,
			MaximumArtifactSizeBytes = Math.Min(source.MaximumArtifactSizeBytes, traceLimit),
			BatchReceived = source.BatchReceived,
			BatchReceivedError = source.BatchReceivedError,
			OutputFormat = source.OutputFormat,
		};
	}

	private void CompleteCore()
	{
		var finalContext = GetTestContextSafely();
		var failed = observedFailure is not null || finalContext.HasFailed;
		if (failed)
			CaptureFailureArtifacts();

		recording?.Dispose();
		CopyRecordingDiagnostics();
		var hasReportableDiagnostics = diagnostics.Snapshot().Any(static diagnostic => diagnostic.Severity is AppDriverDiagnosticSeverity.Warning or AppDriverDiagnosticSeverity.Error);
		var retain = failed || hasReportableDiagnostics || options.RetentionPolicy == DiagnosticsRetentionPolicy.KeepAll;
		if (failed && options.Mode == AutomaticDiagnosticsMode.FailureOnly && recording is not null)
		{
			var tracePath = Path.Combine(sessionDirectory, "semantic-trace" + SemanticRecordingFrameWriter.GetDefaultExtension(options.Recording.OutputFormat));
			var remaining = GetRemainingArtifactBytes(reserveForManifest: true);
			var flushedPath = remaining > 0 ? recording.FlushBuffered(tracePath, remaining) : null;
			if (flushedPath is not null)
				AddExistingArtifact("semantic-trace", flushedPath, "Buffered semantic recording flushed after failure.");
		}
		else if (recording?.OutputPath is string continuousTrace && File.Exists(continuousTrace))
		{
			AddExistingArtifact("semantic-trace", continuousTrace, "Continuous semantic recording.");
		}

		if (retain)
		{
			WriteProcessLogs();
			WriteDiagnosticsLog();
			WriteManifest(failed, finalContext);
			AttachArtifacts();
		}
		else
		{
			DeleteSuccessfulArtifacts();
		}

		ApplyRetentionPolicy();
	}

	private void CaptureFailureArtifacts()
	{
		if (options.CaptureFinalScreenshotOnFailure)
		{
			try
			{
				var response = driver.CaptureScreenshot("png");
				DriverCommandClient.ThrowIfFailure(response, "Final diagnostics screenshot failed.");
				var bytes = Convert.FromBase64String(response.BytesBase64 ?? string.Empty);
				TryWriteArtifact("final-screenshot", "final-screenshot.png", bytes, "Final screenshot captured after failure.");
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				RecordDiagnostic(AppDriverDiagnosticSeverity.Warning, "final-screenshot-failed", "The final diagnostics screenshot could not be captured.", ex);
			}
		}

		if (options.CaptureFinalTreeOnFailure)
		{
			try
			{
				var snapshot = driver.GetVisualTree();
				TryWriteTextArtifact("final-tree", "final-tree.json", JsonConvert.SerializeObject(snapshot, JsonSettings), "Final visual tree captured after failure.");
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				RecordDiagnostic(AppDriverDiagnosticSeverity.Warning, "final-tree-failed", "The final diagnostics visual tree could not be captured.", ex);
			}
		}
	}

	private void WriteProcessLogs()
	{
		if (!options.IncludeProcessLogs)
			return;
		if (!string.IsNullOrWhiteSpace(driver.Connection.LastStartupLog))
			TryWriteTextArtifact("injector-log", "injector.log", driver.Connection.LastStartupLog!, "Injector startup log.");
		if (PayloadDiagnosticsPaths.TryReadPayloadLogTail(driver.Connection.PipeName, driver.Connection.TargetProcess.Id, out var payloadLog, maxCharacters: 64 * 1024))
			TryWriteTextArtifact("payload-log", "payload.log", payloadLog, "Payload process log tail.");
	}

	private void WriteDiagnosticsLog()
	{
		var entries = diagnostics.Snapshot();
		if (entries.Count == 0 && observedFailure is null)
			return;
		var payload = new
		{
			failure = observedFailure?.ToString(),
			entries = entries.Select(entry => new
			{
				entry.TimestampUtc,
				severity = entry.Severity.ToString(),
				entry.Code,
				entry.Message,
				exception = entry.Exception?.ToString(),
			}),
		};
		TryWriteTextArtifact("protocol-log", "diagnostics.json", JsonConvert.SerializeObject(payload, JsonSettings), "Driver, protocol, and artifact diagnostics.");
	}

	private void WriteManifest(bool failed, DiagnosticsTestContext testContext)
	{
		try
		{
			Directory.CreateDirectory(sessionDirectory);
			var assembly = typeof(AppDriver).Assembly;
			var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			var manifest = new DiagnosticsArtifactManifest
			{
				SchemaVersion = 1,
				CreatedUtc = DateTimeOffset.UtcNow,
				TestName = testContext.TestName,
				Failed = failed,
				Mode = options.Mode.ToString(),
				ProcessId = driver.Connection.TargetProcess.Id,
				ProcessName = SafeGetProcessName(driver.Connection.TargetProcess),
				ClientVersion = informationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown",
				PayloadVersion = informationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown",
				PayloadFrameworkFamily = driver.Connection.PayloadFrameworkFamily,
				ProtocolVersion = ProtocolConstants.ProtocolVersion,
				Failure = observedFailure?.ToString(),
				Artifacts = artifacts.ToArray(),
			};
			var json = JsonConvert.SerializeObject(manifest, JsonSettings);
			var bytes = Encoding.UTF8.GetBytes(json);
			if (bytes.LongLength > GetRemainingArtifactBytes(reserveForManifest: false))
			{
				RecordDiagnostic(AppDriverDiagnosticSeverity.Warning, "manifest-size-limit", "The diagnostics manifest exceeded the remaining artifact budget.");
				return;
			}
			ManifestPath = Path.Combine(sessionDirectory, "manifest.json");
			File.WriteAllBytes(ManifestPath, bytes);
			artifacts.Add(new DiagnosticsArtifactManifestEntry { Kind = "manifest", FileName = Path.GetFileName(ManifestPath), SizeBytes = bytes.LongLength, Description = "Diagnostics artifact manifest." });
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			RecordDiagnostic(AppDriverDiagnosticSeverity.Error, "manifest-write-failed", "The diagnostics manifest could not be written.", ex);
		}
	}

	private void AttachArtifacts()
	{
		foreach (var artifact in artifacts.ToArray())
		{
			var path = Path.Combine(sessionDirectory, artifact.FileName);
			if (!File.Exists(path) && configuredTracePath is not null && artifact.Kind == "semantic-trace")
				path = configuredTracePath;
			try
			{
				sink.AttachArtifact(path, artifact.Description);
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				RecordDiagnostic(AppDriverDiagnosticSeverity.Warning, "artifact-attachment-failed", $"The diagnostics artifact '{artifact.FileName}' could not be attached to the test result.", ex);
			}
		}
	}

	private bool TryWriteTextArtifact(string kind, string fileName, string text, string description) =>
		TryWriteArtifact(kind, fileName, Encoding.UTF8.GetBytes(text ?? string.Empty), description);

	private bool TryWriteArtifact(string kind, string fileName, byte[] bytes, string description)
	{
		if (bytes.LongLength > GetRemainingArtifactBytes(reserveForManifest: true))
		{
			RecordDiagnostic(AppDriverDiagnosticSeverity.Warning, "artifact-size-limit", $"The diagnostics artifact '{fileName}' was omitted because it would exceed the per-test size limit.");
			return false;
		}
		try
		{
			Directory.CreateDirectory(sessionDirectory);
			var path = Path.Combine(sessionDirectory, fileName);
			File.WriteAllBytes(path, bytes);
			artifacts.Add(new DiagnosticsArtifactManifestEntry { Kind = kind, FileName = fileName, SizeBytes = bytes.LongLength, Description = description });
			return true;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			RecordDiagnostic(AppDriverDiagnosticSeverity.Warning, "artifact-write-failed", $"The diagnostics artifact '{fileName}' could not be written.", ex);
			return false;
		}
	}

	private void AddExistingArtifact(string kind, string path, string description)
	{
		try
		{
			var info = new FileInfo(path);
			artifacts.Add(new DiagnosticsArtifactManifestEntry
			{
				Kind = kind,
				FileName = Path.GetFileName(path),
				SizeBytes = info.Exists ? info.Length : 0,
				Description = description,
			});
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			RecordDiagnostic(AppDriverDiagnosticSeverity.Warning, "artifact-inspection-failed", $"The diagnostics artifact '{path}' could not be inspected.", ex);
		}
	}

	private long GetRemainingArtifactBytes(bool reserveForManifest)
	{
		var used = artifacts.Sum(static artifact => artifact.SizeBytes);
		var reserve = reserveForManifest ? Math.Min(64 * 1024, Math.Max(1024, options.MaximumArtifactSizeBytes / 10)) : 0;
		return Math.Max(0, options.MaximumArtifactSizeBytes - used - reserve);
	}

	private void CopyRecordingDiagnostics()
	{
		if (recording is null)
			return;
		foreach (var entry in recording.Diagnostics)
			RecordDiagnostic(entry.Severity, entry.Code, entry.Message, entry.Exception);
	}

	private void RecordDiagnostic(AppDriverDiagnosticSeverity severity, string code, string message, Exception? exception = null)
	{
		var diagnostic = new AppDriverDiagnostic { Severity = severity, Code = code, Message = message, Exception = exception };
		diagnostics.Add(diagnostic);
		try
		{
			sink.Log(diagnostic);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
		}
	}

	private DiagnosticsTestContext GetTestContextSafely()
	{
		try
		{
			return sink.GetCurrentTestContext() ?? new DiagnosticsTestContext();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			return new DiagnosticsTestContext();
		}
	}

	private string ResolveArtifactRoot(DiagnosticsTestContext context)
	{
		var root = string.IsNullOrWhiteSpace(options.OutputDirectory)
			? Path.Combine(context.ResultsDirectory, "DeepFlowTest")
			: Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.OutputDirectory));
		return Path.GetFullPath(root);
	}

	private void ApplyRetentionPolicy()
	{
		try
		{
			if (!Directory.Exists(artifactRoot))
				return;
			var directories = new DirectoryInfo(artifactRoot).GetDirectories("dft-*")
				.OrderByDescending(static directory => directory.LastWriteTimeUtc)
				.ToArray();
			var cutoff = options.MaximumArtifactAge is TimeSpan maximumAge ? DateTime.UtcNow - maximumAge : DateTime.MinValue;
			foreach (var directory in directories.Where((directory, index) => index >= options.MaximumRetainedSessions || directory.LastWriteTimeUtc < cutoff))
			{
				if (!string.Equals(directory.FullName, sessionDirectory, StringComparison.OrdinalIgnoreCase))
					directory.Delete(recursive: true);
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			RecordDiagnostic(AppDriverDiagnosticSeverity.Warning, "artifact-retention-failed", "The diagnostics retention policy could not be fully applied.", ex);
		}
	}

	private void DeleteSuccessfulArtifacts()
	{
		try
		{
			if (configuredTracePath is not null && File.Exists(configuredTracePath))
				File.Delete(configuredTracePath);
			if (configuredTracePath is null && Directory.Exists(sessionDirectory))
				Directory.Delete(sessionDirectory, recursive: true);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			RecordDiagnostic(AppDriverDiagnosticSeverity.Warning, "artifact-cleanup-failed", "Successful-test diagnostics could not be removed.", ex);
		}
	}

	private static string CreateSessionDirectoryName(string testName, int processId)
	{
		var safeName = SanitizeFileName(testName);
		if (safeName.Length > 80)
			safeName = safeName.Substring(safeName.Length - 80);
		return $"dft-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{safeName}-{processId}";
	}

	private static string SanitizeFileName(string value)
	{
		var invalid = Path.GetInvalidFileNameChars();
		var characters = (value ?? "test").Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '_' : character).ToArray();
		var sanitized = new string(characters).Trim('_');
		return string.IsNullOrWhiteSpace(sanitized) ? "test" : sanitized;
	}

	private static string SafeGetProcessName(ITargetProcess process)
	{
		try
		{
			return string.IsNullOrWhiteSpace(process.ProcessName) ? "process" : process.ProcessName;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			return "process";
		}
	}
}

internal sealed class DiagnosticsArtifactManifest
{
	public int SchemaVersion { get; init; }
	public DateTimeOffset CreatedUtc { get; init; }
	public string TestName { get; init; } = string.Empty;
	public bool Failed { get; init; }
	public string Mode { get; init; } = string.Empty;
	public int ProcessId { get; init; }
	public string ProcessName { get; init; } = string.Empty;
	public string ClientVersion { get; init; } = string.Empty;
	public string PayloadVersion { get; init; } = string.Empty;
	public string PayloadFrameworkFamily { get; init; } = string.Empty;
	public string ProtocolVersion { get; init; } = string.Empty;
	public string? Failure { get; init; }
	public IReadOnlyList<DiagnosticsArtifactManifestEntry> Artifacts { get; init; } = [];
}

internal sealed class DiagnosticsArtifactManifestEntry
{
	public string Kind { get; init; } = string.Empty;
	public string FileName { get; init; } = string.Empty;
	public long SizeBytes { get; init; }
	public string Description { get; init; } = string.Empty;
}
