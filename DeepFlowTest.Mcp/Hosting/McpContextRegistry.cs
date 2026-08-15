namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using Microsoft.Extensions.Options;

internal sealed class McpContextRegistry : IDisposable
{
	private readonly object gate = new();
	private readonly McpSnapshotCache snapshotCache;
	private readonly McpStreamRegistry streamRegistry;
	private readonly McpElementHandleRegistry elementHandles;
	private readonly IOptions<McpServerOptions> options;
	private readonly Dictionary<string, ContextState> contexts = new(StringComparer.Ordinal);
	private string? selectedLegacyContextId;
	private bool disposed;

	public McpContextRegistry(
		McpSnapshotCache snapshotCache,
		McpStreamRegistry streamRegistry,
		McpElementHandleRegistry elementHandles,
		IOptions<McpServerOptions> options)
	{
		this.snapshotCache = snapshotCache ?? throw new ArgumentNullException(nameof(snapshotCache));
		this.streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
		this.elementHandles = elementHandles ?? throw new ArgumentNullException(nameof(elementHandles));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
	}

	public McpSession? SelectedLegacySession
	{
		get
		{
			lock (gate)
				return selectedLegacyContextId is not null && contexts.TryGetValue(selectedLegacyContextId, out var state)
					? state.Session
					: null;
		}
	}

	public string? SelectedLegacyContextId
	{
		get
		{
			lock (gate)
				return selectedLegacyContextId;
		}
	}

	public McpTargetStatus Open(McpSession session, bool selectForLegacy)
	{
		ArgumentNullException.ThrowIfNull(session);
		var now = DateTimeOffset.UtcNow;
		List<ContextState> cleanup;
		ContextState state;
		lock (gate)
		{
			ObjectDisposedException.ThrowIf(disposed, this);
			cleanup = ClaimExpiredContexts(now);
			var contextId = ToContextId(session.SessionId);
			state = new ContextState(
				contextId,
				session,
				CopyPolicy(options.Value.Policy),
				now,
				idleExpirationEnabled: !selectForLegacy);
			contexts.Add(contextId, state);

			if (selectForLegacy)
			{
				var previousContextId = selectedLegacyContextId;
				selectedLegacyContextId = contextId;
				if (previousContextId is not null
					&& !string.Equals(previousContextId, contextId, StringComparison.Ordinal)
					&& contexts.Remove(previousContextId, out var previous))
				{
					ClaimCleanup(previous, cleanup);
				}
			}
		}

		Cleanup(cleanup);
		return CreateStatus(state, includeActivity: state.IdleExpirationEnabled);
	}

	public McpContextLookup GetSelectedLegacy(bool touch = false)
	{
		string? contextId;
		lock (gate)
			contextId = selectedLegacyContextId;

		return contextId is null ? McpContextLookup.Missing : Get(contextId, touch);
	}

	public McpContextLookup Get(string contextId, bool touch = true)
	{
		var now = DateTimeOffset.UtcNow;
		List<ContextState> cleanup;
		ContextState? state;
		McpTargetStatus? exitedStatus = null;
		lock (gate)
		{
			if (disposed)
				return McpContextLookup.Missing;

			cleanup = ClaimExpiredContexts(now);
			if (!contexts.TryGetValue(contextId, out state))
				state = null;
			else if (state.IsExited)
				exitedStatus = state.TerminalStatus!;
			else if (touch)
				state.LastActivityUtc = now;
		}

		Cleanup(cleanup);
		if (state is null)
			return McpContextLookup.Missing;
		if (exitedStatus is not null)
			return McpContextLookup.Exited(state, exitedStatus);

		if (!state.Session.IsAlive)
			return MarkExited(state);

		lock (gate)
		{
			if (!contexts.TryGetValue(contextId, out var current) || !ReferenceEquals(current, state))
				return McpContextLookup.Missing;
			if (current.IsExited)
				return McpContextLookup.Exited(current, current.TerminalStatus!);
		}

		return McpContextLookup.Active(state, CreateStatus(state, includeActivity: state.IdleExpirationEnabled, knownAlive: true));
	}

	public McpContextCloseResult Close(string contextId)
	{
		ContextState? state;
		bool wasSelected;
		List<ContextState> cleanup = [];
		lock (gate)
		{
			wasSelected = string.Equals(selectedLegacyContextId, contextId, StringComparison.Ordinal);
			if (!contexts.Remove(contextId, out state))
				return new McpContextCloseResult(false, wasSelected);

			if (wasSelected)
				selectedLegacyContextId = null;
			ClaimCleanup(state, cleanup);
		}

		Cleanup(cleanup);
		return new McpContextCloseResult(true, wasSelected);
	}

	public bool Touch(string contextId)
	{
		lock (gate)
		{
			if (disposed || !contexts.TryGetValue(contextId, out var state) || state.IsExited)
				return false;

			state.LastActivityUtc = DateTimeOffset.UtcNow;
			return true;
		}
	}

	public McpContextCloseResult CloseSelectedLegacy()
	{
		string? contextId;
		lock (gate)
			contextId = selectedLegacyContextId;

		return contextId is null ? new McpContextCloseResult(false, false) : Close(contextId);
	}

	public int ExpireIdleContexts()
	{
		List<ContextState> cleanup;
		lock (gate)
			cleanup = disposed ? [] : ClaimExpiredContexts(DateTimeOffset.UtcNow);

		Cleanup(cleanup);
		return cleanup.Count;
	}

	public void Dispose()
	{
		List<ContextState> cleanup = [];
		lock (gate)
		{
			if (disposed)
				return;

			disposed = true;
			selectedLegacyContextId = null;
			foreach (var state in contexts.Values)
				ClaimCleanup(state, cleanup);
			contexts.Clear();
		}

		Cleanup(cleanup);
	}

	private McpContextLookup MarkExited(ContextState state)
	{
		var terminalStatus = CreateStatus(state, includeActivity: state.IdleExpirationEnabled, knownAlive: false);
		List<ContextState> cleanup = [];
		lock (gate)
		{
			if (!contexts.TryGetValue(state.ContextId, out var current) || !ReferenceEquals(current, state))
				return McpContextLookup.Missing;

			if (!state.IsExited)
			{
				state.IsExited = true;
				state.TerminalStatus = terminalStatus;
				ClaimCleanup(state, cleanup);
			}
			else
			{
				terminalStatus = state.TerminalStatus!;
			}
		}

		Cleanup(cleanup);
		return McpContextLookup.Exited(state, terminalStatus);
	}

	private List<ContextState> ClaimExpiredContexts(DateTimeOffset now)
	{
		List<ContextState> cleanup = [];
		var timeout = options.Value.ContextIdleTimeoutMs;
		if (timeout <= 0)
			return cleanup;

		var deadline = now.AddMilliseconds(-timeout);
		foreach (var pair in contexts
			.Where(pair => pair.Value.IdleExpirationEnabled && pair.Value.LastActivityUtc <= deadline)
			.ToArray())
		{
			contexts.Remove(pair.Key);
			ClaimCleanup(pair.Value, cleanup);
		}
		return cleanup;
	}

	private static void ClaimCleanup(ContextState state, ICollection<ContextState> cleanup)
	{
		if (state.CleanupClaimed)
			return;

		state.CleanupClaimed = true;
		cleanup.Add(state);
	}

	private void Cleanup(IEnumerable<ContextState> states)
	{
		Exception? firstError = null;
		foreach (var state in states)
		{
			TryCleanup(() => streamRegistry.StopForSession(state.StreamSessionId), ref firstError);
			TryCleanup(() => snapshotCache.Invalidate(state.SnapshotSessionId), ref firstError);
			TryCleanup(() => elementHandles.RemoveContext(state.HandleContextId), ref firstError);
			TryCleanup(state.Session.Dispose, ref firstError);
		}

		if (firstError is not null)
			ExceptionDispatchInfo.Capture(firstError).Throw();
	}

	private static void TryCleanup(Action cleanup, ref Exception? firstError)
	{
		try
		{
			cleanup();
		}
		catch (Exception ex)
		{
			firstError ??= ex;
		}
	}

	private McpTargetStatus CreateStatus(ContextState state, bool includeActivity, bool? knownAlive = null)
	{
		var session = state.Session;
		var isAlive = knownAlive ?? session.IsAlive;
		var lastActivityUtc = includeActivity ? state.LastActivityUtc : (DateTimeOffset?)null;
		return new McpTargetStatus
		{
			ContextId = state.ContextId,
			Revision = snapshotCache.GetLatestRevision(state.SnapshotSessionId),
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

	internal static string ToContextId(Guid sessionId) => $"ctx_{sessionId:N}";

	internal static McpPolicyOptions CopyPolicy(McpPolicyOptions policy) =>
		new()
		{
			AllowLaunch = policy.AllowLaunch,
			AllowActions = policy.AllowActions,
			AllowArbitraryInvoke = policy.AllowArbitraryInvoke,
			AllowFileWrites = policy.AllowFileWrites,
			AllowedExecutableRoots = [.. policy.AllowedExecutableRoots],
			AllowedEnvironmentVariables = [.. policy.AllowedEnvironmentVariables],
		};
}

internal sealed class ContextState(
	string contextId,
	McpSession session,
	McpPolicyOptions policy,
	DateTimeOffset lastActivityUtc,
	bool idleExpirationEnabled)
{
	public string ContextId { get; } = contextId;

	public McpSession Session { get; } = session;

	public McpPolicyOptions Policy { get; } = policy;

	public DateTimeOffset LastActivityUtc { get; set; } = lastActivityUtc;

	public bool IdleExpirationEnabled { get; } = idleExpirationEnabled;

	public Guid StreamSessionId => Session.SessionId;

	public Guid SnapshotSessionId => Session.SessionId;

	public string HandleContextId => ContextId;

	public bool IsExited { get; set; }

	public McpTargetStatus? TerminalStatus { get; set; }

	public bool CleanupClaimed { get; set; }
}

internal sealed record McpContextLookup(ContextState? State, McpTargetStatus? Status, McpContextLookupKind Kind)
{
	public static McpContextLookup Missing { get; } = new(null, null, McpContextLookupKind.Missing);

	public static McpContextLookup Active(ContextState state, McpTargetStatus status) => new(state, status, McpContextLookupKind.Active);

	public static McpContextLookup Exited(ContextState state, McpTargetStatus status) => new(state, status, McpContextLookupKind.Exited);
}

internal enum McpContextLookupKind
{
	Missing,
	Active,
	Exited,
}

internal sealed record McpContextCloseResult(bool Closed, bool WasSelectedLegacy);
