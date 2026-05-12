namespace DeepFlowTest.Contracts;

using System;
using System.Collections.Generic;

public sealed class StandardIpcResponse
{
	public bool? Success { get; set; }

	public string? Status { get; set; }

	public string? ErrorCode { get; set; }

	public string? Error { get; set; }

	public string? LogCorrelationId { get; set; }

	public static StandardIpcResponse Ok() =>
		new() { Success = true, Status = ProtocolConstants.Statuses.Ok };

	public static StandardIpcResponse FromError(string error, string errorCode = ProtocolConstants.ErrorCodes.ProtocolError, string? logCorrelationId = null) =>
		new()
		{
			Success = false,
			Status = ProtocolConstants.Statuses.Error,
			ErrorCode = errorCode,
			Error = error,
			LogCorrelationId = logCorrelationId,
		};
}

public sealed class HelloCommandResponse
{
	public string ProtocolVersion { get; set; } = ProtocolConstants.ProtocolVersion;

	public string? PayloadVersion { get; set; }

	public string PipeName { get; set; } = string.Empty;

	public bool IsReusable { get; set; }

	public int ProcessId { get; set; }

	public string ProcessArchitecture { get; set; } = string.Empty;

	public string FrameworkFamily { get; set; } = string.Empty;

	public DateTimeOffset Timestamp { get; set; }
}

public sealed class PingCommandResponse
{
	public int ProcessId { get; set; }

	public bool IsWpfAvailable { get; set; }

	public bool IsWinFormsAvailable { get; set; }

	public bool IsNativeFallbackAvailable { get; set; }

	public bool IsDispatcherAvailable { get; set; }

	public int RootCount { get; set; }
}

public sealed class PipeStatusCommandResponse
{
	public string PipeName { get; set; } = string.Empty;

	public bool IsReusable { get; set; }

	public bool IsBusy { get; set; }

	public bool IsSending { get; set; }

	public int ActiveSubscriptionCount { get; set; }

	public IReadOnlyList<ActiveSubscriptionResponse> ActiveSubscriptions { get; set; } = Array.Empty<ActiveSubscriptionResponse>();

	public int TotalCommandsHandled { get; set; }

	public int DisconnectedClientCount { get; set; }

	public string IdleMode { get; set; } = "waiting-for-client-or-command";
}

public sealed class ActiveSubscriptionResponse
{
	public string SubscriptionId { get; set; } = string.Empty;

	public string Kind { get; set; } = string.Empty;

	public string? ConnectionId { get; set; }
}
