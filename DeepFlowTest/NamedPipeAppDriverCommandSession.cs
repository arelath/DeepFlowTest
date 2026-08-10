namespace DeepFlowTest;

using System;
using System.Threading;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal sealed class NamedPipeAppDriverCommandSession : IUnsafeAppDriverCommandSession, IAppDriverStreamingSession
{
	private readonly AppConnection connection;
	private readonly AppDriverOptions options;
	private readonly SemaphoreSlim sendLock = new(1, 1);

	public NamedPipeAppDriverCommandSession(AppConnection connection, AppDriverOptions options)
	{
		this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		options.Validate();
	}

	public TResponse Send<TResponse>(IpcCommand command)
	{
		sendLock.Wait();
		try
		{
			using var client = new NamedPipeClient(
				connection.PipeName,
				getTargetExitCode: () => connection.TargetProcess.ExitCode,
				readTargetCrashLog: () => PayloadDiagnosticsPaths.TryReadCrashLog(connection.PipeName, out var crashLog) ? crashLog : null);
			var timeoutMs = DurationUtility.ToMilliseconds(options.Timeout, nameof(options.Timeout));
			// Have the payload honour the same timeout the client is waiting for. Without this, the
			// payload falls back to its short default command timeout, which is too tight for slow UI
			// actions like Click on a complex WPF menu.
			var commandWithTimeout = command with { TimeoutMs = command.TimeoutMs ?? timeoutMs };
			var response = client.Send(commandWithTimeout, timeoutMs);
			return MessagePacker.ConvertTo<TResponse>(response);
		}
		finally
		{
			sendLock.Release();
		}
	}

	IAppDriverStreamSession IAppDriverStreamingSession.StartStream(StartSendingCommandRequest command, int timeoutMs) =>
		NamedPipeAppDriverStreamSession.Create(
			connection.PipeName,
			command,
			() => connection.TargetProcess.ExitCode,
			Math.Max(1, timeoutMs));
}
