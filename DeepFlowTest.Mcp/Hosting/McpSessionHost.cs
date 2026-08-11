namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

internal sealed class McpSessionHost : IDisposable
{
	private readonly object gate = new();
	private readonly McpTargetSessionFactory sessionFactory;
	private readonly McpSnapshotCache snapshotCache;
	private readonly McpStreamRegistry streamRegistry;
	private readonly IMcpActivitySink? activity;
	private readonly IOptions<McpServerOptions> options;
	private readonly McpElementHandleRegistry? elementHandles;
	private readonly Dictionary<string, ContextState> contexts = new(StringComparer.Ordinal);
	private McpSession? current;

	public McpSessionHost(
		McpTargetSessionFactory sessionFactory,
		McpSnapshotCache snapshotCache,
		McpStreamRegistry streamRegistry)
		: this(sessionFactory, snapshotCache, streamRegistry, activity: null, Options.Create(new McpServerOptions()), elementHandles: null)
	{
	}

	public McpSessionHost(
		McpTargetSessionFactory sessionFactory,
		McpSnapshotCache snapshotCache,
		McpStreamRegistry streamRegistry,
		IMcpActivitySink? activity)
		: this(sessionFactory, snapshotCache, streamRegistry, activity, Options.Create(new McpServerOptions()), elementHandles: null)
	{
	}

	public McpSessionHost(
		McpTargetSessionFactory sessionFactory,
		McpSnapshotCache snapshotCache,
		McpStreamRegistry streamRegistry,
		IMcpActivitySink? activity,
		IOptions<McpServerOptions> options)
		: this(sessionFactory, snapshotCache, streamRegistry, activity, options, elementHandles: null)
	{
	}

	public McpSessionHost(
		McpTargetSessionFactory sessionFactory,
		McpSnapshotCache snapshotCache,
		McpStreamRegistry streamRegistry,
		IMcpActivitySink? activity,
		IOptions<McpServerOptions> options,
		McpElementHandleRegistry? elementHandles)
	{
		this.sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
		this.snapshotCache = snapshotCache ?? throw new ArgumentNullException(nameof(snapshotCache));
		this.streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
		this.activity = activity;
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.elementHandles = elementHandles;
	}

	public McpTargetStatus Status
	{
		get
		{
			lock (gate)
			{
				if (current is not null && !current.IsAlive)
				{
					streamRegistry.StopForSession(current.SessionId);
					snapshotCache.Invalidate(current.SessionId);
					elementHandles?.RemoveContext(ToContextId(current.SessionId));
				}

				return ToStatus(current, lastActivityUtc: null);
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

	public McpTargetStatus AttachContext(McpTargetSelector selector, int? timeoutMs = null, bool noInject = false, string? pipeId = null)
	{
		var session = sessionFactory.Attach(selector, timeoutMs, noInject, pipeId);
		return AddContext(session, "context.attach");
	}

	public McpTargetStatus LaunchContext(McpLaunchOptions launchOptions)
	{
		var session = sessionFactory.Launch(launchOptions);
		return AddContext(session, "context.launch");
	}

	public McpTargetStatus Detach()
	{
		lock (gate)
		{
			if (current is not null)
			{
				streamRegistry.StopForSession(current.SessionId);
				snapshotCache.Invalidate(current.SessionId);
				elementHandles?.RemoveContext(ToContextId(current.SessionId));
			}
			current?.Dispose();
			current = null;
			var status = ToStatus(null, lastActivityUtc: null);
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
				streamRegistry.StopForSession(current.SessionId);
				snapshotCache.Invalidate(current.SessionId);
				elementHandles?.RemoveContext(ToContextId(current.SessionId));
				throw new CliException(CliErrorCodes.TargetExited, $"Target process {current.Target.ProcessId} has exited.");
			}

			return current;
		}
	}

	public McpSession RequireContext(string contextId)
	{
		if (string.IsNullOrWhiteSpace(contextId))
			throw new CliException(CliErrorCodes.InvalidArguments, "contextId is required.");

		lock (gate)
		{
			CleanupExpiredContexts();
			if (contexts.TryGetValue(contextId, out var state))
			{
				if (!state.Session.IsAlive)
				{
					RemoveContextCore(contextId, state);
					throw new CliException(CliErrorCodes.TargetExited, $"Target process {state.Session.Target.ProcessId} has exited.");
				}

				state.LastActivityUtc = DateTimeOffset.UtcNow;
				return state.Session;
			}

			if (current is not null && string.Equals(contextId, ToContextId(current.SessionId), StringComparison.Ordinal))
			{
				if (!current.IsAlive)
				{
					streamRegistry.StopForSession(current.SessionId);
					snapshotCache.Invalidate(current.SessionId);
					elementHandles?.RemoveContext(contextId);
					throw new CliException(CliErrorCodes.TargetExited, $"Target process {current.Target.ProcessId} has exited.");
				}

				return current;
			}
		}

		throw new CliException(CliErrorCodes.StaleTarget, $"Context '{contextId}' is not active. Open a new context and retry.");
	}

	public McpTargetStatus GetContextStatus(string contextId)
	{
		var session = RequireContext(contextId);
		lock (gate)
		{
			var lastActivity = contexts.TryGetValue(contextId, out var state) ? state.LastActivityUtc : (DateTimeOffset?)null;
			return ToStatus(session, lastActivity);
		}
	}

	public bool TryGetContextStatus(string contextId, out McpTargetStatus status)
	{
		try
		{
			status = GetContextStatus(contextId);
			return true;
		}
		catch (CliException)
		{
			status = new McpTargetStatus { ContextId = contextId, Attached = false, IsAlive = false };
			return false;
		}
	}

	public McpPolicyOptions GetContextPolicy(string contextId)
	{
		_ = RequireContext(contextId);
		lock (gate)
			return contexts.TryGetValue(contextId, out var state) ? state.Policy : CopyPolicy(options.Value.Policy);
	}

	public McpTargetStatus CloseContext(string contextId)
	{
		if (string.IsNullOrWhiteSpace(contextId))
			throw new CliException(CliErrorCodes.InvalidArguments, "contextId is required.");

		lock (gate)
		{
			if (!contexts.Remove(contextId, out var state))
			{
				if (current is not null && string.Equals(contextId, ToContextId(current.SessionId), StringComparison.Ordinal))
					return Detach();

				throw new CliException(CliErrorCodes.StaleTarget, $"Context '{contextId}' is not active.");
			}

			streamRegistry.StopForSession(state.Session.SessionId);
			snapshotCache.Invalidate(state.Session.SessionId);
			elementHandles?.RemoveContext(contextId);
			state.Session.Dispose();
			var status = ToStatus(null, lastActivityUtc: null);
			PublishTarget("context.close", "close", status);
			return status;
		}
	}

	public TResponse Send<TResponse>(IpcCommand command, int timeoutMs)
	{
		var session = RequireSession();
		return Send<TResponse>(session, command, timeoutMs);
	}

	public TResponse Send<TResponse>(string contextId, IpcCommand command, int timeoutMs)
	{
		var session = RequireContext(contextId);
		return Send<TResponse>(session, command, timeoutMs);
	}

	private static TResponse Send<TResponse>(McpSession session, IpcCommand command, int timeoutMs)
	{
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
		McpSession[] sessions;
		lock (gate)
		{
			foreach (var contextId in contexts.Keys)
				elementHandles?.RemoveContext(contextId);
			if (current is not null)
				elementHandles?.RemoveContext(ToContextId(current.SessionId));
			sessions = contexts.Values.Select(static state => state.Session)
				.Concat(current is null ? [] : [current])
				.DistinctBy(static session => session.SessionId)
				.ToArray();
			contexts.Clear();
			current = null;
			streamRegistry.StopAll();
			snapshotCache.Invalidate();
		}

		foreach (var session in sessions)
			session.Dispose();
	}

	private McpTargetStatus ReplaceCurrent(McpSession session)
	{
		lock (gate)
		{
			if (current is not null)
			{
				streamRegistry.StopForSession(current.SessionId);
				snapshotCache.Invalidate(current.SessionId);
				elementHandles?.RemoveContext(ToContextId(current.SessionId));
			}
			current?.Dispose();
			current = session;
			return ToStatus(current, lastActivityUtc: null);
		}
	}

	private McpTargetStatus AddContext(McpSession session, string activityKind)
	{
		lock (gate)
		{
			CleanupExpiredContexts();
			var contextId = ToContextId(session.SessionId);
			var state = new ContextState(session, DateTimeOffset.UtcNow, CopyPolicy(options.Value.Policy));
			contexts[contextId] = state;
			var status = ToStatus(session, state.LastActivityUtc);
			PublishTarget(activityKind, session.Source, status);
			return status;
		}
	}

	private void CleanupExpiredContexts()
	{
		var timeout = options.Value.ContextIdleTimeoutMs;
		if (timeout <= 0)
			return;

		var deadline = DateTimeOffset.UtcNow.AddMilliseconds(-timeout);
		foreach (var pair in contexts.Where(pair => pair.Value.LastActivityUtc <= deadline).ToArray())
		{
			contexts.Remove(pair.Key);
			RemoveContextCore(pair.Key, pair.Value);
		}
	}

	private void RemoveContextCore(string contextId, ContextState state)
	{
		contexts.Remove(contextId);
		streamRegistry.StopForSession(state.Session.SessionId);
		snapshotCache.Invalidate(state.Session.SessionId);
		elementHandles?.RemoveContext(contextId);
		state.Session.Dispose();
	}

	private McpTargetStatus ToStatus(McpSession? session, DateTimeOffset? lastActivityUtc)
	{
		if (session is null)
			return new McpTargetStatus { Attached = false, IsAlive = false };

		var isAlive = session.IsAlive;
		return new McpTargetStatus
		{
			ContextId = ToContextId(session.SessionId),
			Revision = snapshotCache.GetLatestRevision(session.SessionId),
			LastActivityUtc = lastActivityUtc,
			ExpiresAtUtc = lastActivityUtc?.AddMilliseconds(Math.Max(0, options.Value.ContextIdleTimeoutMs)),
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

	private static string ToContextId(Guid sessionId) => $"ctx_{sessionId:N}";

	private static McpPolicyOptions CopyPolicy(McpPolicyOptions policy) =>
		new()
		{
			AllowLaunch = policy.AllowLaunch,
			AllowActions = policy.AllowActions,
			AllowArbitraryInvoke = policy.AllowArbitraryInvoke,
			AllowFileWrites = policy.AllowFileWrites,
			AllowedExecutableRoots = [.. policy.AllowedExecutableRoots],
			AllowedEnvironmentVariables = [.. policy.AllowedEnvironmentVariables],
		};

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

	private sealed class ContextState(McpSession session, DateTimeOffset lastActivityUtc, McpPolicyOptions policy)
	{
		public McpSession Session { get; } = session;

		public DateTimeOffset LastActivityUtc { get; set; } = lastActivityUtc;

		public McpPolicyOptions Policy { get; } = policy;
	}
}
