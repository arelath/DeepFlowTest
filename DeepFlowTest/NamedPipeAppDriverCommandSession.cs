namespace DeepFlowTest;

using System;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class NamedPipeAppDriverCommandSession(AppConnection connection, AppDriverOptions options) : IAppDriverCommandSession, IAppDriverStreamingSession
{
	private readonly AppConnection connection = connection ?? throw new ArgumentNullException(nameof(connection));
	private readonly AppDriverOptions options = options ?? throw new ArgumentNullException(nameof(options));

	public TResponse Send<TResponse>(IpcCommand command)
	{
		using var client = new NamedPipeClient(
			connection.PipeName,
			getTargetExitCode: () => connection.TargetProcess.ExitCode,
			readTargetCrashLog: () => PayloadCrashLog.TryRead(connection.PipeName, out var crashLog) ? crashLog : null);
		var timeoutMs = (int)Math.Max(1, options.Timeout.TotalMilliseconds);
		// Have the payload honour the same timeout the client is waiting for. Without this, the
		// payload falls back to its short default command timeout, which is too tight for slow UI
		// actions like Click on a complex WPF menu.
		command.TimeoutMs ??= timeoutMs;
		var response = client.Send(command, timeoutMs);
		return MessagePacker.ConvertTo<TResponse>(response);
	}

	IAppDriverStreamSession IAppDriverStreamingSession.StartStream(StartSendingCommandRequest command, int timeoutMs) =>
		NamedPipeAppDriverStreamSession.Create(
			connection.PipeName,
			command,
			() => connection.TargetProcess.ExitCode,
			Math.Max(1, timeoutMs));
}
