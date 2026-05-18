namespace DeepFlowTest.Mcp.Contracts;

using System.IO;
using DeepFlowTest.Cli;

internal sealed record class McpTargetSelector
{
	public int? ProcessId { get; init; }

	public string? ProcessName { get; init; }

	public string? ExecutablePath { get; init; }

	public string? WindowTitle { get; init; }

	public bool IsEmpty =>
		!ProcessId.HasValue
		&& string.IsNullOrWhiteSpace(ProcessName)
		&& string.IsNullOrWhiteSpace(ExecutablePath)
		&& string.IsNullOrWhiteSpace(WindowTitle);

	public TargetSelector ToCliSelector() =>
		new()
		{
			ProcessId = ProcessId,
			ProcessName = string.IsNullOrWhiteSpace(ProcessName)
				? Path.GetFileNameWithoutExtension(ExecutablePath ?? string.Empty)
				: ProcessName,
			WindowTitle = WindowTitle,
		};
}
