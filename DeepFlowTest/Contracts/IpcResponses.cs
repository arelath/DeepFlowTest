namespace DeepFlowTest.Contracts;

using System;
using System.Collections.Generic;

public sealed class StandardIpcResponse
{
	public bool? Success { get; set; }

	public string? Status { get; set; }

	public string? ErrorCode { get; set; }

	public string? Error { get; set; }

	public object? Value { get; set; }

	public string? LogCorrelationId { get; set; }

	public static StandardIpcResponse Ok() =>
		new() { Success = true, Status = ProtocolConstants.Statuses.Ok };

	public static StandardIpcResponse PendingResult(string? logCorrelationId = null) =>
		new() { Success = true, Status = ProtocolConstants.Statuses.PendingResult, LogCorrelationId = logCorrelationId };

	public static StandardIpcResponse UnserializableResult() =>
		new() { Success = true, Status = ProtocolConstants.Statuses.UnserializableResult };

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

	public IReadOnlyList<ActiveSubscriptionResponse> ActiveSubscriptions { get; set; } = [];

	public int TotalCommandsHandled { get; set; }

	public int DisconnectedClientCount { get; set; }

	public string IdleMode { get; set; } = "waiting-for-client-or-command";

	public Dictionary<string, long> Counters { get; set; } = [];
}

public sealed class FindElementCommandResponse
{
	public bool Success { get; set; } = true;

	public string Status { get; set; } = ProtocolConstants.Statuses.Ok;

	public List<FindElementMatchResponse> Matches { get; set; } = [];

	public int MatchCount { get; set; }

	public int MaxMatches { get; set; }
}

public sealed class FindElementMatchResponse
{
	public string TargetId { get; set; } = string.Empty;

	public string TypeName { get; set; } = string.Empty;

	public string? FrameworkTypeName { get; set; }

	public Dictionary<string, object?> Properties { get; set; } = [];
}

public sealed class ScreenshotCommandResponse
{
	public bool Success { get; set; } = true;

	public string Status { get; set; } = ProtocolConstants.Statuses.Ok;

	public string? ErrorCode { get; set; }

	public string? Error { get; set; }

	public string? LogCorrelationId { get; set; }

	public string TargetId { get; set; } = string.Empty;

	public string Format { get; set; } = "png";

	public int Width { get; set; }

	public int Height { get; set; }

	public int ByteCount { get; set; }

	public string BytesBase64 { get; set; } = string.Empty;
}

public sealed class ActiveSubscriptionResponse
{
	public string SubscriptionId { get; set; } = string.Empty;

	public string Kind { get; set; } = string.Empty;

	public string? ConnectionId { get; set; }

	public int IntervalMs { get; set; }

	public long LastSequenceNumber { get; set; }
}

public sealed class StartSendingCommandResponse
{
	public string SubscriptionId { get; set; } = string.Empty;

	public string StreamKind { get; set; } = string.Empty;

	public string Status { get; set; } = ProtocolConstants.Statuses.Started;

	public int IntervalMs { get; set; }

	public long SequenceStart { get; set; }
}

public sealed class StopSendingCommandResponse
{
	public string SubscriptionId { get; set; } = string.Empty;

	public string Status { get; set; } = ProtocolConstants.Statuses.Stopped;
}

public sealed class StreamMessage
{
	public string MessageKind { get; set; } = "stream";

	public string SubscriptionId { get; set; } = string.Empty;

	public string StreamKind { get; set; } = string.Empty;

	public long SequenceNumber { get; set; }

	public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

	public object? Data { get; set; }

	public CliStreamError? Error { get; set; }
}

public sealed class CliStreamError
{
	public string Code { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;
}
