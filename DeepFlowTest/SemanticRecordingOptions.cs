namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using DeepFlowTest.Contracts;

public sealed class SemanticRecordingOptions
{
	private SemanticRecordingOutputFormat outputFormat = SemanticRecordingOutputFormat.CondensedAgent;

	public int IntervalMs { get; set; } = TimeoutDefaults.StreamIntervalMs;

	public IReadOnlyList<string>? PropNames { get; set; }

	public string? RootTargetId { get; set; }

	public bool IncludeInitialSnapshot { get; set; } = true;

	public int TextIdleMs { get; set; } = 400;

	public int MaxQueuedActions { get; set; } = 1000;

	public int MaxBatchFrames { get; set; } = 100;

	public int MaxNodeCount { get; set; } = VisualTreeDefaults.DefaultMaxNodeCount;

	public int? TimeoutMs { get; set; }

	public Action<SemanticRecordingBatch>? BatchReceived { get; set; }

	public Action<Exception>? BatchReceivedError { get; set; }

	public SemanticRecordingOutputFormat OutputFormat
	{
		get => outputFormat;
		set => outputFormat = value;
	}

	public bool CompactOutput
	{
		get => outputFormat == SemanticRecordingOutputFormat.CompactJson;
		set => outputFormat = value ? SemanticRecordingOutputFormat.CompactJson : SemanticRecordingOutputFormat.RawJson;
	}
}
