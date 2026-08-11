namespace DeepFlowTest.Mcp.Configuration;

using System.Collections.Generic;
using DeepFlowTest.Contracts;

internal sealed class McpServerOptions
{
	public McpToolProfile ToolProfile { get; set; } = McpToolProfile.Agent;

	public int DefaultTimeoutMs { get; set; } = TimeoutDefaults.CliCommandTimeoutMs;

	public int AttachTimeoutMs { get; set; } = TimeoutDefaults.CliAttachTimeoutMs;

	public int CacheTtlMs { get; set; } = 500;

	public int TreeLimit { get; set; } = 1_000;

	public int StreamBufferSize { get; set; } = 32;

	public int ResourceRetentionLimit { get; set; } = 16;

	public int ActivityRetentionLimit { get; set; } = 256;

	public string? ActivityLogFile { get; set; }

	public int ContextIdleTimeoutMs { get; set; } = 30 * 60 * 1_000;

	public IReadOnlyList<string> DefaultProperties { get; set; } = KnownProperties.DefaultVisualTreePropertyNames;

	public McpHttpOptions Http { get; set; } = new();

	public McpPolicyOptions Policy { get; set; } = new();

	public McpStartupOptions Startup { get; set; } = new();
}

internal enum McpToolProfile
{
	Agent,
	Full,
}

internal sealed class McpHttpOptions
{
	public string Host { get; set; } = "127.0.0.1";

	public int Port { get; set; } = 4153;

	public string Path { get; set; } = "/mcp";

	public bool EnableLegacySse { get; set; }

	public string? EndpointFile { get; set; }

	public bool StartMinimized { get; set; }

	public string AllowedHosts => "localhost;127.0.0.1;[::1]";
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
