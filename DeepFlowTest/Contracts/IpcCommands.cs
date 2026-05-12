namespace DeepFlowTest.Contracts;

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
