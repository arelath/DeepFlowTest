namespace DeepFlowTest;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

public enum AutomaticDiagnosticsMode
{
	FailureOnly,
	Always,
	Manual,
	Off,
}

public enum DiagnosticsRetentionPolicy
{
	KeepFailuresOnly,
	KeepAll,
	DeleteOnSuccess,
}

public enum AppDriverDiagnosticSeverity
{
	Information,
	Warning,
	Error,
}

public sealed class AppDriverDiagnostic
{
	public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

	public AppDriverDiagnosticSeverity Severity { get; init; }

	public string Code { get; init; } = string.Empty;

	public string Message { get; init; } = string.Empty;

	public Exception? Exception { get; init; }
}

public sealed class DiagnosticsTestContext
{
	public string ResultsDirectory { get; init; } = Path.GetTempPath();

	public string TestName { get; init; } = "test";

	public bool HasFailed { get; init; }
}

public interface IDiagnosticsArtifactSink
{
	DiagnosticsTestContext GetCurrentTestContext();

	void AttachArtifact(string path, string description);

	void Log(AppDriverDiagnostic diagnostic);
}

public sealed class AutomaticDiagnosticsOptions
{
	public AutomaticDiagnosticsMode Mode { get; init; } = AutomaticDiagnosticsMode.FailureOnly;

	public string? OutputDirectory { get; init; }

	public long MaximumArtifactSizeBytes { get; init; } = 16 * 1024 * 1024;

	public long FailureBufferSizeBytes { get; init; } = 8 * 1024 * 1024;

	public DiagnosticsRetentionPolicy RetentionPolicy { get; init; } = DiagnosticsRetentionPolicy.KeepFailuresOnly;

	public TimeSpan? MaximumArtifactAge { get; init; } = TimeSpan.FromDays(14);

	public int MaximumRetainedSessions { get; init; } = 50;

	public bool CaptureFinalScreenshotOnFailure { get; init; } = true;

	public bool CaptureFinalTreeOnFailure { get; init; } = true;

	public bool IncludeProcessLogs { get; init; } = true;

	public SemanticRecordingOptions Recording { get; init; } = new();

	public IDiagnosticsArtifactSink? ArtifactSink { get; init; }

	internal void Validate()
	{
		if (!Enum.IsDefined(typeof(AutomaticDiagnosticsMode), Mode))
			throw new ArgumentOutOfRangeException(nameof(Mode));
		if (!Enum.IsDefined(typeof(DiagnosticsRetentionPolicy), RetentionPolicy))
			throw new ArgumentOutOfRangeException(nameof(RetentionPolicy));
		if (MaximumArtifactSizeBytes <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaximumArtifactSizeBytes));
		if (FailureBufferSizeBytes <= 0 || FailureBufferSizeBytes > MaximumArtifactSizeBytes)
			throw new ArgumentOutOfRangeException(nameof(FailureBufferSizeBytes), "The failure buffer must be positive and cannot exceed the per-test artifact limit.");
		if (MaximumArtifactAge is TimeSpan maximumAge && maximumAge <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(MaximumArtifactAge));
		if (MaximumRetainedSessions <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaximumRetainedSessions));
		(Recording ?? throw new ArgumentNullException(nameof(Recording))).Validate();
	}
}

internal sealed class AppDriverDiagnosticsCollector
{
	private readonly ConcurrentQueue<AppDriverDiagnostic> entries = new();

	public IReadOnlyList<AppDriverDiagnostic> Snapshot() => entries.ToArray();

	public void Add(AppDriverDiagnostic diagnostic)
	{
		_ = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
		entries.Enqueue(diagnostic);
	}
}
