namespace DeepFlowTest.Mcp.Contracts;

internal sealed record class McpToolResponse
{
	public bool Success { get; init; }

	public McpTargetStatus? Target { get; init; }

	public object? Data { get; init; }

	public McpToolError? Error { get; init; }

	public string? Recovery { get; init; }

	public static McpToolResponse Ok(object? data, McpTargetStatus? target = null) =>
		new()
		{
			Success = true,
			Target = target,
			Data = data,
		};

	public static McpToolResponse Fail(string code, string message, object? details = null, string? recovery = null, McpTargetStatus? target = null) =>
		new()
		{
			Success = false,
			Target = target,
			Error = new McpToolError
			{
				Code = code,
				Message = message,
				Details = details,
			},
			Recovery = recovery,
		};
}

internal sealed record class McpToolError
{
	public string Code { get; init; } = string.Empty;

	public string Message { get; init; } = string.Empty;

	public object? Details { get; init; }
}

internal sealed record class McpTargetStatus
{
	public bool Attached { get; init; }

	public string? SessionId { get; init; }

	public int? ProcessId { get; init; }

	public string? ProcessName { get; init; }

	public string? MainWindowTitle { get; init; }

	public string? Architecture { get; init; }

	public string? FrameworkFamily { get; init; }

	public string? ProtocolVersion { get; init; }

	public string? Source { get; init; }

	public bool LaunchedByServer { get; init; }

	public bool TerminateOnDetach { get; init; }

	public bool IsAlive { get; init; }

	public string? ExitReason { get; init; }
}
