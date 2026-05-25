namespace DeepFlowTest.Mcp.Hosting;

using System;

internal sealed record class McpEndpointInfo
{
	public string State { get; init; } = "stopped";

	public string? StreamableHttpUrl { get; init; }

	public string? LegacySseUrl { get; init; }

	public string? Error { get; init; }

	public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
