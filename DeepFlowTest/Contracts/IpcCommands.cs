namespace DeepFlowTest.Contracts;

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public abstract record class IpcCommand
{
	protected IpcCommand(string kind, int? timeoutMs = null)
	{
		Kind = kind;
		TimeoutMs = timeoutMs;
	}

	public string Kind { get; set; }

	public int? TimeoutMs { get; set; }
}

public sealed record class HelloCommandRequest : IpcCommand
{
	public HelloCommandRequest()
		: base(ProtocolConstants.Commands.Hello)
	{
	}

	public HelloCommandRequest(int? timeoutMs = null)
		: this()
	{
		TimeoutMs = timeoutMs;
	}

	public string ProtocolVersion { get; set; } = ProtocolConstants.ProtocolVersion;
}

public sealed record class PingCommandRequest : IpcCommand
{
	public PingCommandRequest()
		: base(ProtocolConstants.Commands.Ping)
	{
	}

	public PingCommandRequest(int? timeoutMs = null)
		: this()
	{
		TimeoutMs = timeoutMs;
	}
}

public sealed record class PipeStatusCommandRequest : IpcCommand
{
	public PipeStatusCommandRequest()
		: base(ProtocolConstants.Commands.PipeStatus)
	{
	}

	public PipeStatusCommandRequest(int? timeoutMs = null)
		: this()
	{
		TimeoutMs = timeoutMs;
	}
}

public sealed record class ConfigureDiagnosticsCommandRequest : IpcCommand
{
	public ConfigureDiagnosticsCommandRequest()
		: base(ProtocolConstants.Commands.ConfigureDiagnostics)
	{
	}

	public VirtualPointerOptionsDto? VirtualPointer { get; set; }
}

public sealed record class VirtualPointerOptionsDto
{
	public bool Enabled { get; set; }

	public bool ShowClickRipples { get; set; } = true;

	public bool ShowDragTrail { get; set; } = true;

	public int HideDelayMs { get; set; } = 800;

	public bool IncludeInScreenshots { get; set; }
}

public sealed record class GetVisualTreeCommandRequest : IpcCommand
{
	public GetVisualTreeCommandRequest()
		: base(ProtocolConstants.Commands.GetVisualTree)
	{
	}

	public GetVisualTreeCommandRequest(IReadOnlyList<string>? propNames = null, bool asSnapshot = false, int? timeoutMs = null)
		: this()
	{
		PropNames = propNames;
		AsSnapshot = asSnapshot;
		TimeoutMs = timeoutMs;
	}

	private IReadOnlyList<string>? propNames;

	public IReadOnlyList<string>? PropNames
	{
		get => propNames;
		set => propNames = value?.ToArray();
	}

	public bool AsSnapshot { get; set; }

	public string? RootTargetId { get; set; }

	public bool IncludeHidden { get; set; } = true;

	public int? MaxDepth { get; set; }

	public int? MaxNodeCount { get; set; }
}

public sealed record class GetBindingFailuresCommandRequest : IpcCommand
{
	public GetBindingFailuresCommandRequest()
		: base(ProtocolConstants.Commands.GetBindingFailures)
	{
	}

	public GetBindingFailuresCommandRequest(long? afterSequenceNumber, int maxCount = 1000, int? timeoutMs = null)
		: this()
	{
		AfterSequenceNumber = afterSequenceNumber;
		MaxCount = maxCount;
		TimeoutMs = timeoutMs;
	}

	public long? AfterSequenceNumber { get; set; }

	public int MaxCount { get; set; } = 1000;
}

public sealed record class FindElementCommandRequest : IpcCommand
{
	public FindElementCommandRequest()
		: base(ProtocolConstants.Commands.FindElement)
	{
	}

	public FindElementCommandRequest(IReadOnlyList<string>? propNames, object? matcherCode, int maxMatches = 1, int? timeoutMs = null)
		: this()
	{
		PropNames = propNames;
		MatcherCode = matcherCode;
		MaxMatches = maxMatches;
		TimeoutMs = timeoutMs;
	}

	private IReadOnlyList<string>? propNames;

	public IReadOnlyList<string>? PropNames
	{
		get => propNames;
		set => propNames = value?.ToArray();
	}

	public ElementSelectorDto? Selector { get; set; }

	public string? RootTargetId { get; set; }

	public bool IncludeRoot { get; set; } = true;

	public int? MaxDepth { get; set; }

	public int? MaxNodeCount { get; set; }

	public object? MatcherCode { get; set; }

	public string? MatcherHash { get; set; }

	public object? RootMatcherCode { get; set; }

	public string? RootMatcherHash { get; set; }

	public int MaxMatches { get; set; } = 1;
}

public sealed record class ElementSelectorDto
{
	public string? TypeName { get; set; }

	public string? Name { get; set; }

	public string? AutomationId { get; set; }

	public string? Text { get; set; }

	public string? Content { get; set; }

	public Dictionary<string, object?> Properties { get; set; } = new();
}

public sealed record class ScreenshotCommandRequest : IpcCommand
{
	public ScreenshotCommandRequest()
		: base(ProtocolConstants.Commands.Screenshot)
	{
	}

	public ScreenshotCommandRequest(string format, string? targetId = null, int? timeoutMs = null)
		: this(ImageFormatExtensions.ParseProtocolString(format), targetId, timeoutMs)
	{
	}

	public ScreenshotCommandRequest(ImageFormat format, string? targetId = null, int? timeoutMs = null)
		: this()
	{
		Format = format;
		TargetId = targetId;
		TimeoutMs = timeoutMs;
	}

	[JsonConverter(typeof(ProtocolImageFormatJsonConverter))]
	public ImageFormat Format { get; set; } = ImageFormat.Png;

	public string? TargetId { get; set; }
}

public abstract record class TargetedIpcCommand : IpcCommand
{
	protected TargetedIpcCommand(string kind)
		: base(kind)
	{
	}

	protected TargetedIpcCommand(string kind, string targetId, int? timeoutMs = null)
		: base(kind, timeoutMs)
	{
		TargetId = targetId;
	}

	public string TargetId { get; set; } = string.Empty;
}

public sealed record class ClickCommandRequest : TargetedIpcCommand
{
	public ClickCommandRequest()
		: base(ProtocolConstants.Commands.Click)
	{
	}

	public ClickCommandRequest(string targetId, string mouseButton, int? timeoutMs = null)
		: this(targetId, ProtocolValueMapper.ParseMouseButton(mouseButton), timeoutMs)
	{
	}

	public ClickCommandRequest(string targetId, MouseButtonKind mouseButton, int? timeoutMs = null)
		: base(ProtocolConstants.Commands.Click, targetId, timeoutMs)
	{
		MouseButton = mouseButton;
	}

	[JsonConverter(typeof(ProtocolMouseButtonJsonConverter))]
	public MouseButtonKind MouseButton { get; set; } = MouseButtonKind.Left;

	public int ClickCount { get; set; } = 1;
}

public sealed record class DragAndDropCommandRequest : TargetedIpcCommand
{
	public DragAndDropCommandRequest()
		: base(ProtocolConstants.Commands.DragAndDrop)
	{
	}

	public DragAndDropCommandRequest(string sourceTargetId, string destinationTargetId, int? timeoutMs = null)
		: base(ProtocolConstants.Commands.DragAndDrop, sourceTargetId, timeoutMs)
	{
		DestinationTargetId = destinationTargetId;
	}

	public string DestinationTargetId { get; set; } = string.Empty;

	public int DurationMs { get; set; } = 500;

	public int HoldMs { get; set; } = 75;

	public int StepIntervalMs { get; set; } = 16;

	public int PostDropWaitMs { get; set; } = 100;

	public double SourceAnchorX { get; set; } = 0.5;

	public double SourceAnchorY { get; set; } = 0.5;

	public double DestinationAnchorX { get; set; } = 0.5;

	public double DestinationAnchorY { get; set; } = 0.5;

	public bool UseInjectedEvents { get; set; }

	public bool EnsureForeground { get; set; } = true;

	public bool ValidateSameProcess { get; set; } = true;
}

public sealed record class FocusCommandRequest : TargetedIpcCommand
{
	public FocusCommandRequest()
		: base(ProtocolConstants.Commands.Focus)
	{
	}

	public FocusCommandRequest(string targetId, int? timeoutMs = null)
		: base(ProtocolConstants.Commands.Focus, targetId, timeoutMs)
	{
	}
}

public sealed record class TypeTextCommandRequest : IpcCommand
{
	public TypeTextCommandRequest()
		: base(ProtocolConstants.Commands.TypeText)
	{
	}

	public TypeTextCommandRequest(string text, string? targetId = null, bool clearFirst = false, int? timeoutMs = null)
		: this()
	{
		Text = text;
		TargetId = targetId;
		ClearFirst = clearFirst;
		TimeoutMs = timeoutMs;
	}

	public string Text { get; set; } = string.Empty;

	public string? TargetId { get; set; }

	public bool ClearFirst { get; set; }
}

public sealed record class KeyPressCommandRequest : IpcCommand
{
	public KeyPressCommandRequest()
		: base(ProtocolConstants.Commands.KeyPress)
	{
	}

	public KeyPressCommandRequest(object? keys, int delayMs = TimeoutDefaults.KeyboardDelayMs, int? timeoutMs = null)
		: this()
	{
		Keys = keys;
		DelayMs = delayMs;
		TimeoutMs = timeoutMs;
	}

	public object? Keys { get; set; }

	public string? TargetId { get; set; }

	public int DelayMs { get; set; } = TimeoutDefaults.KeyboardDelayMs;

	public bool EnsureForeground { get; set; } = true;
}

public sealed record class SetPropertyCommandRequest : TargetedIpcCommand
{
	public SetPropertyCommandRequest()
		: base(ProtocolConstants.Commands.SetProperty)
	{
	}

	public SetPropertyCommandRequest(string targetId, string propertyName, object? propertyValue, int? timeoutMs = null)
		: base(ProtocolConstants.Commands.SetProperty, targetId, timeoutMs)
	{
		PropertyName = propertyName;
		PropertyValue = propertyValue;
	}

	public string PropertyName { get; set; } = string.Empty;

	public object? PropertyValue { get; set; }
}

public sealed record class RaiseEventCommandRequest : TargetedIpcCommand
{
	public RaiseEventCommandRequest()
		: base(ProtocolConstants.Commands.RaiseEvent)
	{
	}

	public RaiseEventCommandRequest(string targetId, object? getRoutedEventArgs, int? timeoutMs = null)
		: base(ProtocolConstants.Commands.RaiseEvent, targetId, timeoutMs)
	{
		GetRoutedEventArgs = getRoutedEventArgs;
	}

	public object? GetRoutedEventArgs { get; set; }

	public string EventName { get; set; } = string.Empty;
}

public sealed record class KnownRoutedEventCommandRequest : TargetedIpcCommand
{
	public KnownRoutedEventCommandRequest()
		: base(ProtocolConstants.Commands.KnownRoutedEvent)
	{
	}

	public KnownRoutedEventCommandRequest(string targetId, string eventName, int? timeoutMs = null)
		: base(ProtocolConstants.Commands.KnownRoutedEvent, targetId, timeoutMs)
	{
		EventName = eventName;
	}

	public string EventName { get; set; } = string.Empty;
}

public sealed record class KnownOperationCommandRequest : TargetedIpcCommand
{
	public KnownOperationCommandRequest()
		: base(ProtocolConstants.Commands.KnownOperation)
	{
	}

	public KnownOperationCommandRequest(string targetId, string operation, int? timeoutMs = null)
		: base(ProtocolConstants.Commands.KnownOperation, targetId, timeoutMs)
	{
		Operation = operation;
	}

	public string Operation { get; set; } = string.Empty;
}

public sealed record class InvokeCommandRequest : TargetedIpcCommand
{
	public InvokeCommandRequest()
		: base(ProtocolConstants.Commands.Invoke)
	{
	}

	public InvokeCommandRequest(string targetId, object? code, int? timeoutMs = null)
		: base(ProtocolConstants.Commands.Invoke, targetId, timeoutMs)
	{
		Code = code;
		AllowUnsafeCode = true;
	}

	public object? Code { get; set; }

	public bool AllowUnsafeCode { get; set; }
}

public sealed record class StartSendingCommandRequest : IpcCommand
{
	public StartSendingCommandRequest()
		: base(ProtocolConstants.Commands.StartSending)
	{
	}

	public StartSendingCommandRequest(
		string streamKind,
		int intervalMs = TimeoutDefaults.StreamIntervalMs,
		IReadOnlyList<string>? propNames = null,
		string format = "png",
		string? targetId = null,
		int? timeoutMs = null)
		: this(streamKind, intervalMs, propNames, ImageFormatExtensions.ParseProtocolString(format), targetId, timeoutMs)
	{
	}

	public StartSendingCommandRequest(
		string streamKind,
		int intervalMs,
		IReadOnlyList<string>? propNames,
		ImageFormat format,
		string? targetId = null,
		int? timeoutMs = null)
		: this()
	{
		StreamKind = streamKind;
		IntervalMs = intervalMs;
		PropNames = propNames;
		Format = format;
		TargetId = targetId;
		TimeoutMs = timeoutMs;
	}

	public string StreamKind { get; set; } = ProtocolConstants.StreamKinds.VisualTree;

	public int IntervalMs { get; set; } = TimeoutDefaults.StreamIntervalMs;

	private IReadOnlyList<string>? propNames;

	public IReadOnlyList<string>? PropNames
	{
		get => propNames;
		set => propNames = value?.ToArray();
	}

	[JsonConverter(typeof(ProtocolImageFormatJsonConverter))]
	public ImageFormat Format { get; set; } = ImageFormat.Png;

	public string? TargetId { get; set; }

	public SemanticRecordingOptionsDto? SemanticRecording { get; set; }
}

public sealed record class SemanticRecordingOptionsDto
{
	public bool IncludeInitialSnapshot { get; set; } = true;

	public int TextIdleMs { get; set; } = 400;

	public int MaxQueuedActions { get; set; } = 1000;

	public int MaxBatchFrames { get; set; } = 100;

	public int MaxNodeCount { get; set; } = VisualTreeDefaults.DefaultMaxNodeCount;
}

public sealed record class StopSendingCommandRequest : IpcCommand
{
	public StopSendingCommandRequest()
		: base(ProtocolConstants.Commands.StopSending)
	{
	}

	public StopSendingCommandRequest(string subscriptionId, int? timeoutMs = null)
		: this()
	{
		SubscriptionId = subscriptionId;
		TimeoutMs = timeoutMs;
	}

	public string SubscriptionId { get; set; } = string.Empty;
}
