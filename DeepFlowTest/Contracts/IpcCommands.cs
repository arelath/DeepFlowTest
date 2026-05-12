namespace DeepFlowTest.Contracts;

using System.Collections.Generic;

public abstract class IpcCommand
{
	protected IpcCommand(string kind, int? timeoutMs = null)
	{
		Kind = kind;
		TimeoutMs = timeoutMs;
	}

	public string Kind { get; set; }

	public int? TimeoutMs { get; set; }
}

public sealed class HelloCommandRequest : IpcCommand
{
	public HelloCommandRequest()
		: base(ProtocolConstants.Commands.Hello)
	{
	}

	public string ProtocolVersion { get; set; } = ProtocolConstants.ProtocolVersion;
}

public sealed class PingCommandRequest : IpcCommand
{
	public PingCommandRequest()
		: base(ProtocolConstants.Commands.Ping)
	{
	}
}

public sealed class PipeStatusCommandRequest : IpcCommand
{
	public PipeStatusCommandRequest()
		: base(ProtocolConstants.Commands.PipeStatus)
	{
	}
}

public sealed class GetVisualTreeCommandRequest : IpcCommand
{
	public GetVisualTreeCommandRequest()
		: base(ProtocolConstants.Commands.GetVisualTree)
	{
	}

	public IReadOnlyList<string>? PropNames { get; set; }

	public bool AsSnapshot { get; set; }

	public string? RootTargetId { get; set; }

	public bool IncludeHidden { get; set; } = true;

	public int? MaxDepth { get; set; }

	public int? MaxNodeCount { get; set; }
}

public sealed class FindElementCommandRequest : IpcCommand
{
	public FindElementCommandRequest()
		: base(ProtocolConstants.Commands.FindElement)
	{
	}

	public IReadOnlyList<string>? PropNames { get; set; }

	public ElementSelectorDto? Selector { get; set; }

	public object? MatcherCode { get; set; }

	public string? MatcherHash { get; set; }

	public int MaxMatches { get; set; } = 1;
}

public sealed class ElementSelectorDto
{
	public string? TypeName { get; set; }

	public string? Name { get; set; }

	public string? AutomationId { get; set; }

	public string? Text { get; set; }

	public string? Content { get; set; }

	public Dictionary<string, object?> Properties { get; set; } = new();
}

public sealed class ScreenshotCommandRequest : IpcCommand
{
	public ScreenshotCommandRequest()
		: base(ProtocolConstants.Commands.Screenshot)
	{
	}

	public string Format { get; set; } = "png";

	public string? TargetId { get; set; }
}

public abstract class TargetedIpcCommand : IpcCommand
{
	protected TargetedIpcCommand(string kind)
		: base(kind)
	{
	}

	public string TargetId { get; set; } = string.Empty;
}

public sealed class ClickCommandRequest : TargetedIpcCommand
{
	public ClickCommandRequest()
		: base(ProtocolConstants.Commands.Click)
	{
	}

	public string MouseButton { get; set; } = "left";

	public int ClickCount { get; set; } = 1;
}

public sealed class FocusCommandRequest : TargetedIpcCommand
{
	public FocusCommandRequest()
		: base(ProtocolConstants.Commands.Focus)
	{
	}
}

public sealed class TypeTextCommandRequest : IpcCommand
{
	public TypeTextCommandRequest()
		: base(ProtocolConstants.Commands.TypeText)
	{
	}

	public string Text { get; set; } = string.Empty;

	public string? TargetId { get; set; }

	public bool ClearFirst { get; set; }
}

public sealed class KeyPressCommandRequest : IpcCommand
{
	public KeyPressCommandRequest()
		: base(ProtocolConstants.Commands.KeyPress)
	{
	}

	public object? Keys { get; set; }

	public string? TargetId { get; set; }

	public int DelayMs { get; set; } = 50;
}

public sealed class SetPropertyCommandRequest : TargetedIpcCommand
{
	public SetPropertyCommandRequest()
		: base(ProtocolConstants.Commands.SetProperty)
	{
	}

	public string PropertyName { get; set; } = string.Empty;

	public object? PropertyValue { get; set; }
}

public sealed class RaiseEventCommandRequest : TargetedIpcCommand
{
	public RaiseEventCommandRequest()
		: base(ProtocolConstants.Commands.RaiseEvent)
	{
	}

	public object? GetRoutedEventArgs { get; set; }

	public string EventName { get; set; } = string.Empty;
}

public sealed class KnownRoutedEventCommandRequest : TargetedIpcCommand
{
	public KnownRoutedEventCommandRequest()
		: base(ProtocolConstants.Commands.KnownRoutedEvent)
	{
	}

	public string EventName { get; set; } = string.Empty;
}

public sealed class KnownOperationCommandRequest : TargetedIpcCommand
{
	public KnownOperationCommandRequest()
		: base(ProtocolConstants.Commands.KnownOperation)
	{
	}

	public string Operation { get; set; } = string.Empty;
}

public sealed class InvokeCommandRequest : TargetedIpcCommand
{
	public InvokeCommandRequest()
		: base(ProtocolConstants.Commands.Invoke)
	{
	}

	public object? Code { get; set; }

	public bool AllowUnsafeCode { get; set; }
}

public sealed class StartSendingCommandRequest : IpcCommand
{
	public StartSendingCommandRequest()
		: base(ProtocolConstants.Commands.StartSending)
	{
	}

	public string StreamKind { get; set; } = ProtocolConstants.StreamKinds.VisualTree;

	public int IntervalMs { get; set; } = 1000;

	public IReadOnlyList<string>? PropNames { get; set; }

	public string Format { get; set; } = "png";

	public string? TargetId { get; set; }
}

public sealed class StopSendingCommandRequest : IpcCommand
{
	public StopSendingCommandRequest()
		: base(ProtocolConstants.Commands.StopSending)
	{
	}

	public string SubscriptionId { get; set; } = string.Empty;
}
