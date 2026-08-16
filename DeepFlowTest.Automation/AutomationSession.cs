namespace DeepFlowTest.Automation;

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class AutomationAttachOptions
{
	public int TimeoutMs { get; set; } = AutomationTimeoutDefaults.AttachTimeoutMs;

	public bool Debug { get; set; }

	public bool NoInject { get; set; }

	public string? PipeId { get; set; }
}

public interface IAutomationSession : IDisposable
{
	HelloCommandResponse Hello { get; }

	TResponse Send<TResponse>(IpcCommand command, int timeoutMs);

	TResponse Send<TResponse>(IpcCommand command, int timeoutMs, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Send<TResponse>(command, timeoutMs);
	}

	Task<TResponse> SendAsync<TResponse>(IpcCommand command, int timeoutMs, CancellationToken cancellationToken = default) =>
		Task.Run(() => Send<TResponse>(command, timeoutMs, cancellationToken), cancellationToken);

	IAutomationStreamSession StartStream(StartSendingCommandRequest command, int timeoutMs);
}

public interface IAutomationStreamSession : IDisposable
{
	StartSendingCommandResponse Start { get; }

	StreamMessage? ReadFrame(int timeoutMs, CancellationToken cancellationToken = default);
}

public interface IAutomationSessionService
{
	IAutomationSession Open(TargetInfo target, AutomationAttachOptions options);
}

public interface IAutomationSessionConnector
{
	bool TryConnect(AppConnection connection, int timeoutMs, out IAutomationSession? session, out AutomationException? error);
}

public sealed class AutomationSessionService : IAutomationSessionService
{
	private readonly IAutomationSessionConnector connector;
	private readonly Func<AppDriverOptions, IAppConnectionInjector> injectorFactory;

	public AutomationSessionService()
		: this(new NamedPipeAutomationSessionConnector(), options => new ExternalInjectorAppConnectionInjector(options, PayloadStartupModes.ReusableCli))
	{
	}

	public AutomationSessionService(IAutomationSessionConnector connector, Func<AppDriverOptions, IAppConnectionInjector>? injectorFactory = null)
	{
		this.connector = connector ?? throw new ArgumentNullException(nameof(connector));
		this.injectorFactory = injectorFactory ?? CreateDefaultInjector;
	}

	public IAutomationSession Open(TargetInfo target, AutomationAttachOptions options)
	{
		_ = target ?? throw new ArgumentNullException(nameof(target));
		_ = options ?? throw new ArgumentNullException(nameof(options));

		var pipeName = AutomationPipeName.ForTarget(target.ProcessId, options.PipeId);
		var process = target.OpenProcess();
		var connection = AppConnection.ForAttach(process, pipeName, target.FrameworkFamily ?? string.Empty);
		var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(1, options.TimeoutMs));

		if (connector.TryConnect(connection, options.TimeoutMs, out var session, out var error))
			return session!;

		if (error is not null && error.ErrorCode == AutomationErrorCodes.ProtocolError)
		{
			connection.Dispose();
			throw error;
		}

		if (options.NoInject)
		{
			connection.Dispose();
			throw new AutomationException(AutomationErrorCodes.PipeFailed, error?.Message ?? $"Could not connect to pipe '{pipeName}'.");
		}

		try
		{
			var driverOptions = new AppDriverOptions
			{
				PipeName = pipeName,
				Timeout = TimeSpan.FromMilliseconds(Math.Max(1, options.TimeoutMs)),
			};
			var injector = injectorFactory(driverOptions);
			injector.Inject(connection);
		}
		catch (AutomationException)
		{
			connection.Dispose();
			throw;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			connection.Dispose();
			throw new AutomationException(AutomationErrorCodes.AttachFailed, $"Failed to inject reusable automation listener: {ex.Message}");
		}

		do
		{
			if (connector.TryConnect(connection, options.TimeoutMs, out session, out error))
				return session!;

			if (error is not null && error.ErrorCode == AutomationErrorCodes.ProtocolError)
				break;

			Thread.Sleep(Math.Min(AutomationTimeoutDefaults.AttachRetrySleepMs, Math.Max(1, options.TimeoutMs)));
		}
		while (DateTimeOffset.UtcNow < deadline);

		connection.Dispose();
		throw error ?? new AutomationException(AutomationErrorCodes.PipeFailed, $"Could not connect to pipe '{pipeName}' after injection.");
	}

	private static IAppConnectionInjector CreateDefaultInjector(AppDriverOptions options) =>
		new ExternalInjectorAppConnectionInjector(options, PayloadStartupModes.ReusableCli);
}

public sealed class NamedPipeAutomationSessionConnector : IAutomationSessionConnector
{
	public bool TryConnect(AppConnection connection, int timeoutMs, out IAutomationSession? session, out AutomationException? error)
	{
		session = null;
		error = null;
		try
		{
			var created = new NamedPipeAutomationSession(connection, null, timeoutMs);
			var hello = created.Send<HelloCommandResponse>(
				new HelloCommandRequest { ProtocolVersion = ProtocolConstants.ProtocolVersion },
				Math.Max(1, timeoutMs));
			created.ConfigureControlConnection(hello);
			if (!string.Equals(hello.ProtocolVersion, ProtocolConstants.ProtocolVersion, StringComparison.Ordinal))
			{
				created.Dispose();
				error = new AutomationException(AutomationErrorCodes.ProtocolError, $"Protocol mismatch. Expected {ProtocolConstants.ProtocolVersion}, received {hello.ProtocolVersion}.");
				return false;
			}

			created.Hello = hello;
			session = created;
			return true;
		}
		catch (AutomationException ex)
		{
			error = ex;
			return false;
		}
		catch (NamedPipeSessionException ex)
		{
			error = MapNamedPipeException(ex);
			return false;
		}
		catch (Exception ex) when (ex is TimeoutException or System.IO.IOException or ProtocolException)
		{
			error = new AutomationException(AutomationErrorCodes.PipeFailed, ex.Message);
			return false;
		}
	}

	private static AutomationException MapNamedPipeException(NamedPipeSessionException exception)
	{
		return exception.ErrorCode switch
		{
			ProtocolConstants.ErrorCodes.CommandTimeout => new AutomationException(AutomationErrorCodes.CommandTimeout, exception.Message),
			ProtocolConstants.ErrorCodes.TargetExited => new AutomationException(AutomationErrorCodes.TargetExited, exception.Message),
			_ => new AutomationException(AutomationErrorCodes.PipeFailed, exception.Message),
		};
	}
}

public sealed class NamedPipeAutomationSession : IAutomationSession
{
	private readonly AppConnection connection;
	private readonly NamedPipeClient controlClient;
	private bool disposed;
	private bool reuseControlConnection = true;

	public NamedPipeAutomationSession(AppConnection connection, HelloCommandResponse? hello, int connectTimeoutMs = AutomationTimeoutDefaults.OneShotConnectTimeoutCapMs)
	{
		this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
		controlClient = new NamedPipeClient(
			connection.PipeName,
			getTargetExitCode: () => connection.TargetProcess.HasExited ? 0 : null,
			connectTimeoutMs: Math.Min(AutomationTimeoutDefaults.OneShotConnectTimeoutCapMs, Math.Max(1, connectTimeoutMs)),
			connectRetryCount: 1);
		Hello = hello ?? new HelloCommandResponse();
	}

	public HelloCommandResponse Hello { get; internal set; }

	public TResponse Send<TResponse>(IpcCommand command, int timeoutMs)
		=> SendAsync<TResponse>(command, timeoutMs, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();

	public TResponse Send<TResponse>(IpcCommand command, int timeoutMs, CancellationToken cancellationToken)
		=> SendAsync<TResponse>(command, timeoutMs, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

	public async Task<TResponse> SendAsync<TResponse>(IpcCommand command, int timeoutMs, CancellationToken cancellationToken = default)
	{
		try
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(NamedPipeAutomationSession));
			var effectiveTimeoutMs = Math.Max(1, timeoutMs);
			var response = reuseControlConnection
				? await controlClient.SendAsync(command, effectiveTimeoutMs, cancellationToken).ConfigureAwait(false)
				: await SendOneShotAsync(command, effectiveTimeoutMs, cancellationToken).ConfigureAwait(false);
			return MessagePacker.ConvertTo<TResponse>(response);
		}
		catch (NamedPipeSessionException ex)
		{
			throw MapNamedPipeException(ex);
		}
		catch (ProtocolException ex)
		{
			throw new AutomationException(AutomationErrorCodes.ProtocolError, ex.Message);
		}
		catch (IOException ex)
		{
			throw new AutomationException(AutomationErrorCodes.PipeFailed, ex.Message);
		}
	}

	internal void ConfigureControlConnection(HelloCommandResponse hello)
	{
		reuseControlConnection = hello.IsReusable
			&& string.Equals(
				hello.ControlConnectionMode,
				ProtocolConstants.ControlConnectionModes.PersistentSerialized,
				StringComparison.Ordinal);
		if (!reuseControlConnection)
			controlClient.Dispose();
	}

	private async Task<object> SendOneShotAsync(IpcCommand command, int timeoutMs, CancellationToken cancellationToken)
	{
		using var client = new NamedPipeClient(
			connection.PipeName,
			getTargetExitCode: () => connection.TargetProcess.HasExited ? 0 : null,
			connectTimeoutMs: Math.Min(AutomationTimeoutDefaults.OneShotConnectTimeoutCapMs, timeoutMs),
			connectRetryCount: 1);
		return await client.SendAsync(command, timeoutMs, cancellationToken).ConfigureAwait(false);
	}

	public IAutomationStreamSession StartStream(StartSendingCommandRequest command, int timeoutMs) =>
		NamedPipeAutomationStreamSession.Create(
			connection.PipeName,
			command,
			() => connection.TargetProcess.HasExited ? 0 : null,
			Math.Max(1, timeoutMs));

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		controlClient.Dispose();
		connection.Dispose();
	}

	private static AutomationException MapNamedPipeException(NamedPipeSessionException exception)
	{
		return exception.ErrorCode switch
		{
			ProtocolConstants.ErrorCodes.CommandTimeout => new AutomationException(AutomationErrorCodes.CommandTimeout, exception.Message),
			ProtocolConstants.ErrorCodes.TargetExited => new AutomationException(AutomationErrorCodes.TargetExited, exception.Message),
			ProtocolConstants.ErrorCodes.MalformedFrame => new AutomationException(AutomationErrorCodes.ProtocolError, exception.Message),
			_ => new AutomationException(AutomationErrorCodes.PipeFailed, exception.Message),
		};
	}
}

public sealed class NamedPipeAutomationStreamSession : IAutomationStreamSession
{
	private readonly NamedPipeClientStream pipe;
	private readonly Func<int?> getTargetExitCode;

	private NamedPipeAutomationStreamSession(NamedPipeClientStream pipe, Func<int?> getTargetExitCode, StartSendingCommandResponse start)
	{
		this.pipe = pipe;
		this.getTargetExitCode = getTargetExitCode;
		Start = start;
	}

	public StartSendingCommandResponse Start { get; }

	public static NamedPipeAutomationStreamSession Create(
		string pipeName,
		StartSendingCommandRequest command,
		Func<int?> getTargetExitCode,
		int timeoutMs)
	{
		var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		try
		{
			pipe.Connect(Math.Max(1, timeoutMs));
			ThrowIfTargetExited(getTargetExitCode);
			MessagePacker.WriteFrame(pipe, command);
			var response = TimeoutAfter(MessagePacker.ReadFrameAsync(pipe), timeoutMs, CancellationToken.None)
				.ConfigureAwait(false)
				.GetAwaiter()
				.GetResult();
			if (!response.HasFrame || response.Message is null)
				throw new AutomationException(AutomationErrorCodes.PipeFailed, "The stream pipe closed before the start response was received.");
			if (response.Message is StandardIpcResponse standard && standard.Success == false)
				throw MapStandardError(standard);

			return new NamedPipeAutomationStreamSession(
				pipe,
				getTargetExitCode,
				MessagePacker.ConvertTo<StartSendingCommandResponse>(response.Message));
		}
		catch (AutomationException)
		{
			pipe.Dispose();
			throw;
		}
		catch (Exception ex) when (ex is TimeoutException or IOException or ProtocolException)
		{
			pipe.Dispose();
			throw new AutomationException(AutomationErrorCodes.PipeFailed, ex.Message);
		}
	}

	public StreamMessage? ReadFrame(int timeoutMs, CancellationToken cancellationToken = default)
	{
		ThrowIfTargetExited(getTargetExitCode);
		try
		{
			var frame = TimeoutAfter(MessagePacker.ReadFrameAsync(pipe, cancellationToken), timeoutMs, cancellationToken)
				.ConfigureAwait(false)
				.GetAwaiter()
				.GetResult();
			if (!frame.HasFrame || frame.Message is null)
				return null;
			if (frame.Message is StandardIpcResponse standard && standard.Success == false)
				throw MapStandardError(standard);

			return MessagePacker.ConvertTo<StreamMessage>(frame.Message);
		}
		catch (TimeoutException)
		{
			return null;
		}
		catch (ProtocolException ex)
		{
			throw new AutomationException(AutomationErrorCodes.ProtocolError, ex.Message);
		}
		catch (IOException ex)
		{
			throw new AutomationException(AutomationErrorCodes.PipeFailed, ex.Message);
		}
	}

	public void Dispose()
	{
		pipe.Dispose();
	}

	private static void ThrowIfTargetExited(Func<int?> getTargetExitCode)
	{
		var exitCode = getTargetExitCode();
		if (exitCode.HasValue)
			throw new AutomationException(AutomationErrorCodes.TargetExited, $"Target process exited with code {exitCode.Value}.");
	}

	private static AutomationException MapStandardError(StandardIpcResponse response)
	{
		return response.ErrorCode switch
		{
			ProtocolConstants.ErrorCodes.CommandTimeout => new AutomationException(AutomationErrorCodes.CommandTimeout, response.Error ?? "Command timed out.", response),
			ProtocolConstants.ErrorCodes.StaleTarget => new AutomationException(AutomationErrorCodes.StaleTarget, response.Error ?? "Target is stale.", response),
			ProtocolConstants.ErrorCodes.TargetExited => new AutomationException(AutomationErrorCodes.TargetExited, response.Error ?? "Target exited.", response),
			ProtocolConstants.ErrorCodes.UnsupportedTarget => new AutomationException(AutomationErrorCodes.UnsupportedTarget, response.Error ?? "Unsupported target.", response),
			ProtocolConstants.ErrorCodes.InvalidArguments => new AutomationException(AutomationErrorCodes.InvalidArguments, response.Error ?? "Invalid stream request.", response),
			_ => new AutomationException(AutomationErrorCodes.ProtocolError, response.Error ?? "Stream command failed.", response),
		};
	}

	private static async Task<T> TimeoutAfter<T>(Task<T> task, int timeoutMs, CancellationToken cancellationToken)
	{
		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var completed = await Task.WhenAny(task, Task.Delay(Math.Max(1, timeoutMs), timeoutSource.Token)).ConfigureAwait(false);
		if (completed == task)
		{
			timeoutSource.Cancel();
			return await task.ConfigureAwait(false);
		}

		throw new TimeoutException();
	}
}
