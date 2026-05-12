namespace DeepFlowTest;

using System;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class NamedPipeAppDriverCommandSession : IAppDriverCommandSession
{
	private readonly AppConnection connection;
	private readonly AppDriverOptions options;

	public NamedPipeAppDriverCommandSession(AppConnection connection, AppDriverOptions options)
	{
		this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
	}

	public TResponse Send<TResponse>(IpcCommand command)
	{
		using var client = new NamedPipeClient(
			connection.PipeName,
			getTargetExitCode: () => connection.TargetProcess.HasExited ? 0 : null);
		var response = client.Send(command, (int)Math.Max(1, options.Timeout.TotalMilliseconds));
		return MessagePacker.ConvertTo<TResponse>(response);
	}
}
