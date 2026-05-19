namespace DeepFlowTest;

using System.Collections.Generic;
using DeepFlowTest.Contracts;

public sealed class SemanticRecordingOptions
{
	public int IntervalMs { get; set; } = TimeoutDefaults.StreamIntervalMs;

	public IReadOnlyList<string>? PropNames { get; set; }

	public string? RootTargetId { get; set; }

	public bool IncludeInitialSnapshot { get; set; } = true;

	public int TextIdleMs { get; set; } = 400;

	public int MaxQueuedActions { get; set; } = 1000;

	public int MaxBatchFrames { get; set; } = 100;

	public int MaxNodeCount { get; set; } = VisualTreeDefaults.DefaultMaxNodeCount;

	public int? TimeoutMs { get; set; }
}
