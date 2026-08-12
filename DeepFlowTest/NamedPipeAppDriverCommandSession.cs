namespace DeepFlowTest;

using System;
using System.Threading;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal sealed class NamedPipeAppDriverCommandSession : IUnsafeAppDriverCommandSession, IAppDriverStreamingSession, IDisposable
{
	private readonly AppConnection connection;
	private readonly NamedPipeClient controlClient;
	private readonly AppDriverOptions options;
	private readonly SemaphoreSlim sendLock = new(1, 1);
	private bool disposed;
	private bool reuseControlConnection = true;

	public NamedPipeAppDriverCommandSession(AppConnection connection, AppDriverOptions options)
	{
		this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		options.Validate();
		controlClient = new NamedPipeClient(
			connection.PipeName,
			getTargetExitCode: () => connection.TargetProcess.ExitCode,
			readTargetCrashLog: () => PayloadDiagnosticsPaths.TryReadCrashLog(connection.PipeName, out var crashLog) ? crashLog : null);
	}

	public TResponse Send<TResponse>(IpcCommand command)
	{
		sendLock.Wait();
		try
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(NamedPipeAppDriverCommandSession));
			var timeoutMs = DurationUtility.ToMilliseconds(options.Timeout, nameof(options.Timeout));
			// Have the payload honour the same timeout the client is waiting for. Without this, the
			// payload falls back to its short default command timeout, which is too tight for slow UI
			// actions like Click on a complex WPF menu.
			var commandWithTimeout = command with { TimeoutMs = command.TimeoutMs ?? timeoutMs };
			var response = reuseControlConnection
				? controlClient.Send(commandWithTimeout, timeoutMs)
				: SendOneShot(commandWithTimeout, timeoutMs);
			return MessagePacker.ConvertTo<TResponse>(response);
		}
		finally
		{
			sendLock.Release();
		}
	}

	internal HelloCommandResponse NegotiateControlConnection()
	{
		sendLock.Wait();
		try
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(NamedPipeAppDriverCommandSession));

			var timeoutMs = DurationUtility.ToMilliseconds(options.Timeout, nameof(options.Timeout));
			var response = MessagePacker.ConvertTo<HelloCommandResponse>(controlClient.Send(
				new HelloCommandRequest
				{
					ProtocolVersion = ProtocolConstants.ProtocolVersion,
					TimeoutMs = timeoutMs,
				},
				timeoutMs));
			reuseControlConnection = response.IsReusable
				&& string.Equals(
					response.ControlConnectionMode,
					ProtocolConstants.ControlConnectionModes.PersistentSerialized,
					StringComparison.Ordinal);
			if (!reuseControlConnection)
				controlClient.Dispose();
			return response;
		}
		finally
		{
			sendLock.Release();
		}
	}

	private object SendOneShot(IpcCommand command, int timeoutMs)
	{
		using var client = CreateClient();
		return client.Send(command, timeoutMs);
	}

	private NamedPipeClient CreateClient() =>
		new(
			connection.PipeName,
			getTargetExitCode: () => connection.TargetProcess.ExitCode,
			readTargetCrashLog: () => PayloadDiagnosticsPaths.TryReadCrashLog(connection.PipeName, out var crashLog) ? crashLog : null);

	public void Dispose()
	{
		sendLock.Wait();
		try
		{
			if (disposed)
				return;

			disposed = true;
			controlClient.Dispose();
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
