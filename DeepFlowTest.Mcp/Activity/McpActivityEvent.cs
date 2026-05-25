namespace DeepFlowTest.Mcp.Activity;

using System;

internal sealed record class McpActivityEvent
{
	public long Sequence { get; init; }

	public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

	public string Source { get; init; } = "server";

	public string Kind { get; init; } = "event";

	public string Name { get; init; } = string.Empty;

	public string Status { get; init; } = "info";

	public TimeSpan? Duration { get; init; }

	public string? Summary { get; init; }

	public object? Details { get; init; }
}
