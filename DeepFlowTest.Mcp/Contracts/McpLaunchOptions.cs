namespace DeepFlowTest.Mcp.Contracts;

using System.Collections.Generic;

internal sealed record class McpLaunchOptions
{
	public string FileName { get; init; } = string.Empty;

	public string? Arguments { get; init; }

	public string? WorkingDirectory { get; init; }

	public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
		new Dictionary<string, string?>();

	public int? AttachTimeoutMs { get; init; }

	public bool TerminateOnDetach { get; init; }
}
