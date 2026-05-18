namespace DeepFlowTest.Mcp.Configuration;

using System.Collections.Generic;
using DeepFlowTest.Contracts;

internal sealed class McpServerOptions
{
	public int DefaultTimeoutMs { get; set; } = TimeoutDefaults.CliCommandTimeoutMs;

	public int AttachTimeoutMs { get; set; } = TimeoutDefaults.CliAttachTimeoutMs;

	public int CacheTtlMs { get; set; } = 500;

	public int TreeLimit { get; set; } = 1_000;

	public int StreamBufferSize { get; set; } = 32;

	public int ResourceRetentionLimit { get; set; } = 16;

	public IReadOnlyList<string> DefaultProperties { get; set; } = KnownProperties.DefaultVisualTreePropertyNames;

	public McpPolicyOptions Policy { get; set; } = new();

	public McpStartupOptions Startup { get; set; } = new();
}

internal sealed class McpPolicyOptions
{
	public bool AllowLaunch { get; set; }

	public bool AllowActions { get; set; }

	public bool AllowArbitraryInvoke { get; set; }

	public bool AllowFileWrites { get; set; }

	public IReadOnlyList<string> AllowedExecutableRoots { get; set; } = [];

	public IReadOnlyList<string> AllowedEnvironmentVariables { get; set; } = [];
}

internal sealed class McpStartupOptions
{
	public int? ProcessId { get; set; }

	public string? ProcessName { get; set; }

	public string? WindowTitle { get; set; }

	public string? LaunchPath { get; set; }

	public string? LaunchArguments { get; set; }

	public string? WorkingDirectory { get; set; }

	public bool TerminateOnDetach { get; set; }

	public bool NoInject { get; set; }

	public string? PipeId { get; set; }

	public bool HasAttachSelector =>
		ProcessId.HasValue
		|| !string.IsNullOrWhiteSpace(ProcessName)
		|| !string.IsNullOrWhiteSpace(WindowTitle);

	public bool HasLaunchRequest => !string.IsNullOrWhiteSpace(LaunchPath);
}
