namespace DeepFlowTest.Mcp.Hosting;

using System;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Contracts;
using Newtonsoft.Json;

internal sealed class McpSessionHost : IDisposable
{
	private readonly object gate = new();
	private readonly McpTargetSessionFactory sessionFactory;
	private readonly McpSnapshotCache snapshotCache;
	private readonly McpStreamRegistry streamRegistry;
	private readonly IMcpActivitySink? activity;
	private McpSession? current;

	public McpSessionHost(
		McpTargetSessionFactory sessionFactory,
		McpSnapshotCache snapshotCache,
		McpStreamRegistry streamRegistry)
		: this(sessionFactory, snapshotCache, streamRegistry, activity: null)
	{
	}

	public McpSessionHost(
		McpTargetSessionFactory sessionFactory,
		McpSnapshotCache snapshotCache,
		McpStreamRegistry streamRegistry,
		IMcpActivitySink? activity)
	{
		this.sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
		this.snapshotCache = snapshotCache ?? throw new ArgumentNullException(nameof(snapshotCache));
		this.streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
		this.activity = activity;
	}

	public McpTargetStatus Status
	{
		get
		{
			lock (gate)
			{
				if (current is not null && !current.IsAlive)
				{
					streamRegistry.StopAll();
					snapshotCache.Invalidate();
				}

				return ToStatus(current);
			}
		}
	}

	public McpSession? Current
	{
		get
		{
			lock (gate)
				return current;
		}
	}

	public McpTargetStatus Attach(McpTargetSelector selector, int? timeoutMs = null, bool noInject = false, string? pipeId = null)
	{
		var session = sessionFactory.Attach(selector, timeoutMs, noInject, pipeId);
		var status = ReplaceCurrent(session);
		PublishTarget("target.attach", "attach", status);
		return status;
	}

	public McpTargetStatus Launch(McpLaunchOptions launchOptions)
	{
		var session = sessionFactory.Launch(launchOptions);
		var status = ReplaceCurrent(session);
		PublishTarget("target.launch", "launch", status);
		return status;
	}

	public McpTargetStatus Detach()
	{
		lock (gate)
		{
			streamRegistry.StopAll();
			snapshotCache.Invalidate();
			current?.Dispose();
			current = null;
			var status = ToStatus(null);
			PublishTarget("target.detach", "detach", status);
			return status;
		}
	}

	public McpSession RequireSession()
	{
		lock (gate)
		{
			if (current is null)
				throw new CliException(CliErrorCodes.InvalidArguments, "No target is attached. Call deepflow_attach_target or deepflow_launch_target first.");

			if (!current.IsAlive)
			{
				streamRegistry.StopAll();
				snapshotCache.Invalidate();
				throw new CliException(CliErrorCodes.TargetExited, $"Target process {current.Target.ProcessId} has exited.");
			}

			return current;
		}
	}

	public TResponse Send<TResponse>(IpcCommand command, int timeoutMs)
	{
		var session = RequireSession();
		var response = session.AppSession.Send<object>(command, timeoutMs);
		ThrowIfFailedPayloadResponse(response);

		if (response is TResponse typedResponse)
			return typedResponse;

		try
		{
			return MessagePacker.ConvertTo<TResponse>(response);
		}
		catch (Exception ex) when (ex is ProtocolException or InvalidCastException or JsonException)
		{
			throw new CliException(CliErrorCodes.ProtocolError, ex.Message, response);
		}
	}

	public void Dispose()
	{
		Detach();
	}

	private McpTargetStatus ReplaceCurrent(McpSession session)
	{
		lock (gate)
		{
			streamRegistry.StopAll();
			snapshotCache.Invalidate();
			current?.Dispose();
			current = session;
			return ToStatus(current);
		}
	}

	private static McpTargetStatus ToStatus(McpSession? session)
	{
		if (session is null)
			return new McpTargetStatus { Attached = false, IsAlive = false };

		var isAlive = session.IsAlive;
		return new McpTargetStatus
		{
			Attached = true,
			SessionId = session.SessionId.ToString("N"),
			ProcessId = session.Target.ProcessId,
			ProcessName = session.Target.ProcessName,
			MainWindowTitle = session.Target.MainWindowTitle,
			Architecture = session.Target.Architecture,
			FrameworkFamily = session.Target.FrameworkFamily,
			ProtocolVersion = session.AppSession.Hello.ProtocolVersion,
			Source = session.Source,
			LaunchedByServer = session.LaunchedByServer,
			TerminateOnDetach = session.TerminateOnDetach,
			IsAlive = isAlive,
			ExitReason = isAlive ? null : session.ExitCode is { } exitCode ? $"exited:{exitCode}" : "exited",
		};
	}

	private static void ThrowIfFailedPayloadResponse(object response)
	{
		if (!TryConvertFailedPayloadResponse(response, out var standard))
			return;

		throw new CliException(
			ProtocolErrorMapper.Map(standard.ErrorCode),
			standard.Error ?? "Payload command failed.",
			standard);
	}

	private static bool TryConvertFailedPayloadResponse(object response, out StandardIpcResponse standard)
	{
		if (response is StandardIpcResponse direct)
		{
			standard = direct;
			return direct.Success == false;
		}

		try
		{
			var converted = MessagePacker.ConvertTo<StandardIpcResponse>(response);
			standard = converted;
			return converted.Success == false;
		}
		catch (Exception ex) when (ex is ProtocolException or InvalidCastException or JsonException)
		{
			standard = new StandardIpcResponse();
			return false;
		}
	}

	private void PublishTarget(string kind, string name, McpTargetStatus status) =>
		activity?.Publish(new McpActivityEvent
		{
			Source = "server",
			Kind = kind,
			Name = name,
			Status = "success",
			Summary = status.Attached ? $"{status.ProcessName} ({status.ProcessId})" : "No target attached.",
			Details = status,
		});
}
