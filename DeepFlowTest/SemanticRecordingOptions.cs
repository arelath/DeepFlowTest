namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DeepFlowTest.Contracts;

public sealed class SemanticRecordingOptions
{
	private SemanticRecordingOutputFormat outputFormat = SemanticRecordingOutputFormat.CondensedAgent;
	private IReadOnlyList<string>? propNames;

	public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(TimeoutDefaults.StreamIntervalMs);

	public IReadOnlyList<string>? PropNames
	{
		get => propNames;
		init => propNames = value is null ? null : new ReadOnlyCollection<string>(value.ToArray());
	}

	public string? RootTargetId { get; init; }

	public bool IncludeInitialSnapshot { get; init; } = true;

	public TimeSpan TextIdleDuration { get; init; } = TimeSpan.FromMilliseconds(400);

	public int MaxQueuedActions { get; init; } = 1000;

	public int MaxBatchFrames { get; init; } = 100;

	public int MaxNodeCount { get; init; } = VisualTreeDefaults.DefaultMaxNodeCount;

	public TimeSpan? Timeout { get; init; }

	public long MaximumArtifactSizeBytes { get; init; } = 64 * 1024 * 1024;

	public Action<SemanticRecordingBatch>? BatchReceived { get; init; }

	public Action<Exception>? BatchReceivedError { get; init; }

	public SemanticRecordingOutputFormat OutputFormat
	{
		get => outputFormat;
		init => outputFormat = value;
	}

	public bool CompactOutput
	{
		get => outputFormat == SemanticRecordingOutputFormat.CompactJson;
		init => outputFormat = value ? SemanticRecordingOutputFormat.CompactJson : SemanticRecordingOutputFormat.RawJson;
	}

	internal void Validate()
	{
		_ = DurationUtility.ToMilliseconds(Interval, nameof(Interval));
		_ = DurationUtility.ToMilliseconds(TextIdleDuration, nameof(TextIdleDuration), allowZero: true);
		if (Timeout is TimeSpan timeout)
			_ = DurationUtility.ToMilliseconds(timeout, nameof(Timeout));
		if (MaxQueuedActions <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxQueuedActions));
		if (MaxBatchFrames <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxBatchFrames));
		if (MaxNodeCount <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxNodeCount));
		if (MaximumArtifactSizeBytes <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaximumArtifactSizeBytes));
		if (propNames?.Any(static name => string.IsNullOrWhiteSpace(name)) == true)
			throw new ArgumentException("Requested property names cannot contain empty values.", nameof(PropNames));
	}
}
