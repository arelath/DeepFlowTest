namespace DeepFlowTest.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using DeepFlowTest.Interop;

public sealed record class StandardIpcResponse
{
	public StandardIpcResponse()
	{
	}

	public StandardIpcResponse(bool? success = null, string? error = null, string? value = null)
	{
		Success = success;
		Error = error;
		Value = value;
	}

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

	public static StandardIpcResponse StaleElement() =>
		new()
		{
			Success = false,
			Status = ProtocolConstants.Statuses.StaleElement,
			ErrorCode = ProtocolConstants.ErrorCodes.StaleTarget,
			Value = ProtocolConstants.Statuses.StaleElement,
		};

	public static StandardIpcResponse Succeeded(bool success = true) =>
		new() { Success = success, Status = success ? ProtocolConstants.Statuses.Ok : ProtocolConstants.Statuses.Error };

	public static StandardIpcResponse UnserializableResult() =>
		new() { Success = true, Status = ProtocolConstants.Statuses.UnserializableResult };

	public static StandardIpcResponse WithValue(string value) =>
		new() { Value = value };

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

public sealed record class HelloCommandResponse
{
	public HelloCommandResponse()
	{
	}

	public HelloCommandResponse(int protocolVersion, string? payloadVersion, string pipeName, bool isReusable)
	{
		ProtocolVersion = protocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
		PayloadVersion = payloadVersion;
		PipeName = pipeName;
		IsReusable = isReusable;
	}

	public string ProtocolVersion { get; set; } = ProtocolConstants.ProtocolVersion;

	public string? PayloadVersion { get; set; }

	public string PipeName { get; set; } = string.Empty;

	public bool IsReusable { get; set; }

	public int ProcessId { get; set; }

	public string ProcessArchitecture { get; set; } = string.Empty;

	public string FrameworkFamily { get; set; } = string.Empty;

	public DateTimeOffset Timestamp { get; set; }
}

public sealed record class PingCommandResponse
{
	public PingCommandResponse()
	{
	}

	public PingCommandResponse(int rootCount, int nodeCount)
	{
		RootCount = rootCount;
		NodeCount = nodeCount;
	}

	public int ProcessId { get; set; }

	public bool IsWpfAvailable { get; set; }

	public bool IsWinFormsAvailable { get; set; }

	public bool IsNativeFallbackAvailable { get; set; }

	public bool IsDispatcherAvailable { get; set; }

	public int RootCount { get; set; }

	public int NodeCount { get; set; }
}

public sealed record class PipeStatusCommandResponse
{
	public PipeStatusCommandResponse()
	{
	}

	public PipeStatusCommandResponse(
		string pipeName,
		bool isReusable,
		bool isBusy,
		bool isSending,
		IReadOnlyList<ActiveSubscriptionResponse> activeSubscriptions,
		int totalCommandsHandled,
		string idleMode)
	{
		PipeName = pipeName;
		IsReusable = isReusable;
		IsBusy = isBusy;
		IsSending = isSending;
		ActiveSubscriptions = activeSubscriptions;
		ActiveSubscriptionCount = activeSubscriptions.Count;
		TotalCommandsHandled = totalCommandsHandled;
		IdleMode = idleMode;
	}

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

public sealed record class FindElementCommandResponse
{
	public FindElementCommandResponse()
	{
	}

	public FindElementCommandResponse(IReadOnlyList<Dictionary<string, object?>> nodes)
	{
		Nodes = nodes;
	}

	public bool Success { get; set; } = true;

	public string Status { get; set; } = ProtocolConstants.Statuses.Ok;

	public List<FindElementMatchResponse> Matches { get; set; } = [];

	public IReadOnlyList<Dictionary<string, object?>> Nodes
	{
		get => Matches.Select(MatchToNode).ToArray();
		set
		{
			Matches = (value ?? Array.Empty<Dictionary<string, object?>>())
				.Select(NodeToMatch)
				.ToList();
			MatchCount = Matches.Count;
		}
	}

	public int MatchCount { get; set; }

	public int MaxMatches { get; set; }

	private static Dictionary<string, object?> MatchToNode(FindElementMatchResponse match)
	{
		var node = new Dictionary<string, object?>(match.Properties, StringComparer.Ordinal)
		{
			["TargetId"] = match.TargetId,
			["TypeName"] = match.TypeName,
		};
		if (!string.IsNullOrWhiteSpace(match.FrameworkTypeName))
			node["FrameworkTypeName"] = match.FrameworkTypeName;
		return node;
	}

	private static FindElementMatchResponse NodeToMatch(Dictionary<string, object?> node)
	{
		var properties = new Dictionary<string, object?>(node, StringComparer.Ordinal);
		var targetId = ReadString(properties, "TargetId");
		var typeName = ReadString(properties, "TypeName");
		var frameworkTypeName = ReadString(properties, "FrameworkTypeName");
		properties.Remove("TargetId");
		properties.Remove("TypeName");
		properties.Remove("FrameworkTypeName");
		return new FindElementMatchResponse
		{
			TargetId = targetId,
			TypeName = typeName,
			FrameworkTypeName = frameworkTypeName,
			Properties = properties,
		};
	}

	private static string ReadString(Dictionary<string, object?> values, string key) =>
		values.TryGetValue(key, out var value)
			? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
			: string.Empty;
}

public sealed record class FindElementMatchResponse
{
	public string TargetId { get; set; } = string.Empty;

	public string TypeName { get; set; } = string.Empty;

	public string? FrameworkTypeName { get; set; }

	public Dictionary<string, object?> Properties { get; set; } = [];

	public List<ElementPathSegmentResponse> Path { get; set; } = [];
}

public sealed record class ElementPathSegmentResponse
{
	public string TargetId { get; set; } = string.Empty;

	public string TypeName { get; set; } = string.Empty;

	public string? FrameworkTypeName { get; set; }

	public Dictionary<string, object?> Properties { get; set; } = [];
}

public sealed record class ScreenshotCommandResponse
{
	public ScreenshotCommandResponse()
	{
	}

	public ScreenshotCommandResponse(string base64Screenshot)
	{
		Base64Screenshot = base64Screenshot;
		ByteCount = string.IsNullOrEmpty(base64Screenshot)
			? 0
			: Convert.FromBase64String(base64Screenshot).Length;
	}

	public bool Success { get; set; } = true;

	public string Status { get; set; } = ProtocolConstants.Statuses.Ok;

	public string? ErrorCode { get; set; }

	public string? Error { get; set; }

	public string? LogCorrelationId { get; set; }

	public string TargetId { get; set; } = string.Empty;

	[JsonConverter(typeof(ProtocolImageFormatJsonConverter))]
	public ImageFormat Format { get; set; } = ImageFormat.Png;

	public int Width { get; set; }

	public int Height { get; set; }

	public int ByteCount { get; set; }

	public string BytesBase64 { get; set; } = string.Empty;

	public string Base64Screenshot
	{
		get => BytesBase64;
		set => BytesBase64 = value ?? string.Empty;
	}
}

public sealed record class ActiveSubscriptionResponse
{
	public ActiveSubscriptionResponse()
	{
	}

	public ActiveSubscriptionResponse(string subscriptionId, string kind, string? connectionId, int messageCount)
	{
		SubscriptionId = subscriptionId;
		Kind = kind;
		ConnectionId = connectionId;
		MessageCount = messageCount;
	}

	public string SubscriptionId { get; set; } = string.Empty;

	public string Kind { get; set; } = string.Empty;

	public string? ConnectionId { get; set; }

	public int IntervalMs { get; set; }

	public long LastSequenceNumber { get; set; }

	public int MessageCount
	{
		get => LastSequenceNumber > int.MaxValue ? int.MaxValue : (int)LastSequenceNumber;
		set => LastSequenceNumber = value;
	}
}

public sealed record class StartSendingCommandResponse
{
	public StartSendingCommandResponse()
	{
	}

	public StartSendingCommandResponse(string subscriptionId, string streamKind, string status)
	{
		SubscriptionId = subscriptionId;
		StreamKind = streamKind;
		Status = status;
	}

	public string SubscriptionId { get; set; } = string.Empty;

	public string StreamKind { get; set; } = string.Empty;

	public string Status { get; set; } = ProtocolConstants.Statuses.Started;

	public int IntervalMs { get; set; }

	public long SequenceStart { get; set; }
}

public sealed record class StopSendingCommandResponse
{
	public StopSendingCommandResponse()
	{
	}

	public StopSendingCommandResponse(string subscriptionId, string status)
	{
		SubscriptionId = subscriptionId;
		Status = status;
	}

	public string SubscriptionId { get; set; } = string.Empty;

	public string Status { get; set; } = ProtocolConstants.Statuses.Stopped;
}

public sealed record class StreamMessage
{
	public StreamMessage()
	{
	}

	public StreamMessage(string subscriptionId, string kind, long sequence, object? data)
	{
		SubscriptionId = subscriptionId;
		Kind = kind;
		Sequence = sequence;
		Data = data;
	}

	public string MessageKind { get; set; } = "stream";

	public string SubscriptionId { get; set; } = string.Empty;

	public string StreamKind { get; set; } = string.Empty;

	public string Kind
	{
		get => StreamKind;
		set => StreamKind = value ?? string.Empty;
	}

	public long SequenceNumber { get; set; }

	public long Sequence
	{
		get => SequenceNumber;
		set => SequenceNumber = value;
	}

	public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

	public object? Data { get; set; }

	public CliStreamError? Error { get; set; }
}

public sealed record class VisualTreeDeltaSnapshotFrame
{
	public VisualTreeDeltaSnapshotFrame()
	{
	}

	public VisualTreeDeltaSnapshotFrame(VisualTreeSnapshot snapshot)
	{
		Snapshot = snapshot;
	}

	public bool IsDelta { get; set; }

	public VisualTreeSnapshot? Snapshot { get; set; }

	public bool IsFullSnapshot
	{
		get => !IsDelta;
		set => IsDelta = !value;
	}
}

public sealed record class CliStreamError
{
	public string Code { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;
}
