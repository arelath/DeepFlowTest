namespace DeepFlowTest.Cli;

using System;
using DeepFlowTest;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class CliAttachOptions
{
	public int TimeoutMs { get; set; } = 10_000;

	public bool Debug { get; set; }

	public bool NoInject { get; set; }

	public string? PipeId { get; set; }
}

public interface ICliAppSession : IDisposable
{
	HelloCommandResponse Hello { get; }

	TResponse Send<TResponse>(IpcCommand command, int timeoutMs);
}

public interface ICliAppSessionService
{
	ICliAppSession Open(TargetInfo target, CliAttachOptions options);
}

public interface ICliAppSessionConnector
{
	bool TryConnect(AppConnection connection, int timeoutMs, out ICliAppSession? session, out CliException? error);
}

public sealed class CliAppSessionService : ICliAppSessionService
{
	private readonly ICliAppSessionConnector connector;
	private readonly Func<AppDriverOptions, IAppConnectionInjector> injectorFactory;

	public CliAppSessionService()
		: this(new NamedPipeCliAppSessionConnector(), options => new ExternalInjectorAppConnectionInjector(options, PayloadStartupModes.ReusableCli))
	{
	}

	public CliAppSessionService(ICliAppSessionConnector connector, Func<AppDriverOptions, IAppConnectionInjector>? injectorFactory = null)
	{
		this.connector = connector ?? throw new ArgumentNullException(nameof(connector));
		this.injectorFactory = injectorFactory ?? CreateDefaultInjector;
	}

	public ICliAppSession Open(TargetInfo target, CliAttachOptions options)
	{
		_ = target ?? throw new ArgumentNullException(nameof(target));
		_ = options ?? throw new ArgumentNullException(nameof(options));

		var pipeName = CliPipeName.ForTarget(target.ProcessId, options.PipeId);
		var process = target.OpenProcess();
		var connection = AppConnection.ForAttach(process, pipeName, target.FrameworkFamily ?? string.Empty);

		if (connector.TryConnect(connection, options.TimeoutMs, out var session, out var error))
			return session!;

		if (error is not null && error.ErrorCode == CliErrorCodes.ProtocolError)
		{
			connection.Dispose();
			throw error;
		}

		if (options.NoInject)
		{
			connection.Dispose();
			throw new CliException(CliErrorCodes.PipeFailed, error?.Message ?? $"Could not connect to pipe '{pipeName}'.");
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
		catch (CliException)
		{
			connection.Dispose();
			throw;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			connection.Dispose();
			throw new CliException(CliErrorCodes.AttachFailed, $"Failed to inject reusable CLI listener: {ex.Message}");
		}

		if (connector.TryConnect(connection, options.TimeoutMs, out session, out error))
			return session!;

		connection.Dispose();
		throw error ?? new CliException(CliErrorCodes.PipeFailed, $"Could not connect to pipe '{pipeName}' after injection.");
	}

	private static IAppConnectionInjector CreateDefaultInjector(AppDriverOptions options) =>
		new ExternalInjectorAppConnectionInjector(options, PayloadStartupModes.ReusableCli);
}

public sealed class NamedPipeCliAppSessionConnector : ICliAppSessionConnector
{
	public bool TryConnect(AppConnection connection, int timeoutMs, out ICliAppSession? session, out CliException? error)
	{
		session = null;
		error = null;
		try
		{
			var created = new NamedPipeCliAppSession(connection, null);
			var hello = created.Send<HelloCommandResponse>(
				new HelloCommandRequest { ProtocolVersion = ProtocolConstants.ProtocolVersion },
				Math.Max(1, timeoutMs));
			if (!string.Equals(hello.ProtocolVersion, ProtocolConstants.ProtocolVersion, StringComparison.Ordinal))
			{
				created.Dispose();
				error = new CliException(CliErrorCodes.ProtocolError, $"Protocol mismatch. Expected {ProtocolConstants.ProtocolVersion}, received {hello.ProtocolVersion}.");
				return false;
			}

			created.Hello = hello;
			session = created;
			return true;
		}
		catch (CliException ex)
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
			error = new CliException(CliErrorCodes.PipeFailed, ex.Message);
			return false;
		}
	}

	private static CliException MapNamedPipeException(NamedPipeSessionException exception)
	{
		return exception.ErrorCode switch
		{
			ProtocolConstants.ErrorCodes.CommandTimeout => new CliException(CliErrorCodes.CommandTimeout, exception.Message),
			ProtocolConstants.ErrorCodes.TargetExited => new CliException(CliErrorCodes.TargetExited, exception.Message),
			_ => new CliException(CliErrorCodes.PipeFailed, exception.Message),
		};
	}
}

public sealed class NamedPipeCliAppSession : ICliAppSession
{
	private readonly AppConnection connection;

	public NamedPipeCliAppSession(AppConnection connection, HelloCommandResponse? hello)
	{
		this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
		Hello = hello ?? new HelloCommandResponse();
	}

	public HelloCommandResponse Hello { get; internal set; }

	public TResponse Send<TResponse>(IpcCommand command, int timeoutMs)
	{
		using var client = new NamedPipeClient(
			connection.PipeName,
			getTargetExitCode: () => connection.TargetProcess.HasExited ? 0 : null,
			connectTimeoutMs: Math.Min(500, Math.Max(1, timeoutMs)),
			connectRetryCount: 1);
		var response = client.Send(command, Math.Max(1, timeoutMs));
		return MessagePacker.ConvertTo<TResponse>(response);
	}

	public void Dispose()
	{
		connection.Dispose();
	}
}
