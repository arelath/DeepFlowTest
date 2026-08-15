namespace DeepFlowTest.Mcp.Hosting;

using System;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Configuration;
using Newtonsoft.Json;

internal sealed class McpSessionHost : IDisposable
{
	private readonly McpTargetSessionFactory sessionFactory;
	private readonly McpContextRegistry contextRegistry;
	private readonly IMcpActivitySink? activity;

	public McpSessionHost(
		McpTargetSessionFactory sessionFactory,
		McpContextRegistry contextRegistry,
		IMcpActivitySink? activity = null)
	{
		this.sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
		this.contextRegistry = contextRegistry ?? throw new ArgumentNullException(nameof(contextRegistry));
		this.activity = activity;
	}

	public McpTargetStatus Status
	{
		get
		{
			var lookup = contextRegistry.GetSelectedLegacy();
			return lookup.Status ?? UnattachedStatus();
		}
	}

	public McpSession? Current => contextRegistry.SelectedLegacySession;

	public McpTargetStatus Attach(McpTargetSelector selector, int? timeoutMs = null, bool noInject = false, string? pipeId = null)
	{
		var session = sessionFactory.Attach(selector, timeoutMs, noInject, pipeId);
		var status = Open(session, selectForLegacy: true);
		PublishTarget("target.attach", "attach", status);
		return status;
	}

	public McpTargetStatus Launch(McpLaunchOptions launchOptions)
	{
		var session = sessionFactory.Launch(launchOptions);
		var status = Open(session, selectForLegacy: true);
		PublishTarget("target.launch", "launch", status);
		return status;
	}

	public McpTargetStatus AttachContext(McpTargetSelector selector, int? timeoutMs = null, bool noInject = false, string? pipeId = null)
	{
		var session = sessionFactory.Attach(selector, timeoutMs, noInject, pipeId);
		return OpenContext(session, "context.attach");
	}

	public McpTargetStatus LaunchContext(McpLaunchOptions launchOptions)
	{
		var session = sessionFactory.Launch(launchOptions);
		return OpenContext(session, "context.launch");
	}

	public McpTargetStatus Detach()
	{
		contextRegistry.CloseSelectedLegacy();
		var status = UnattachedStatus();
		PublishTarget("target.detach", "detach", status);
		return status;
	}

	public McpSession RequireSession()
	{
		var lookup = contextRegistry.GetSelectedLegacy();
		if (lookup.Kind == McpContextLookupKind.Missing)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "No target is attached. Call deepflow_attach_target or deepflow_launch_target first.");
		return RequireLiveSession(lookup);
	}

	public McpSession RequireContext(string contextId)
	{
		if (string.IsNullOrWhiteSpace(contextId))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "contextId is required.");

		var lookup = contextRegistry.Get(contextId);
		if (lookup.Kind == McpContextLookupKind.Missing)
			throw new AutomationException(AutomationErrorCodes.StaleTarget, $"Context '{contextId}' is not active. Open a new context and retry.");
		return RequireLiveSession(lookup);
	}

	public McpTargetStatus GetContextStatus(string contextId)
	{
		if (string.IsNullOrWhiteSpace(contextId))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "contextId is required.");

		var lookup = contextRegistry.Get(contextId);
		if (lookup.Kind == McpContextLookupKind.Missing)
			throw new AutomationException(AutomationErrorCodes.StaleTarget, $"Context '{contextId}' is not active. Open a new context and retry.");
		return lookup.Status!;
	}

	public bool TryGetContextStatus(string contextId, out McpTargetStatus status)
	{
		try
		{
			status = GetContextStatus(contextId);
			return true;
		}
		catch (AutomationException)
		{
			status = new McpTargetStatus { ContextId = contextId, Attached = false, IsAlive = false };
			return false;
		}
	}

	public McpPolicyOptions GetContextPolicy(string contextId)
	{
		if (string.IsNullOrWhiteSpace(contextId))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "contextId is required.");

		var lookup = contextRegistry.Get(contextId);
		if (lookup.Kind == McpContextLookupKind.Missing)
			throw new AutomationException(AutomationErrorCodes.StaleTarget, $"Context '{contextId}' is not active. Open a new context and retry.");
		if (lookup.Kind == McpContextLookupKind.Exited)
			throw TargetExited(lookup.State!.Session);
		return McpContextRegistry.CopyPolicy(lookup.State!.Policy);
	}

	public McpTargetStatus CloseContext(string contextId)
	{
		if (string.IsNullOrWhiteSpace(contextId))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "contextId is required.");

		var result = contextRegistry.Close(contextId);
		if (!result.Closed)
			throw new AutomationException(AutomationErrorCodes.StaleTarget, $"Context '{contextId}' is not active.");

		var status = UnattachedStatus();
		PublishTarget(result.WasSelectedLegacy ? "target.detach" : "context.close", result.WasSelectedLegacy ? "detach" : "close", status);
		return status;
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
			throw new AutomationException(AutomationErrorCodes.ProtocolError, ex.Message, response);
		}
	}

	public void Dispose()
	{
		contextRegistry.Dispose();
	}

	private McpTargetStatus Open(McpSession session, bool selectForLegacy)
	{
		try
		{
			return contextRegistry.Open(session, selectForLegacy);
		}
		catch
		{
			session.Dispose();
			throw;
		}
	}

	private McpTargetStatus OpenContext(McpSession session, string activityKind)
	{
		var status = Open(session, selectForLegacy: false);
		PublishTarget(activityKind, session.Source, status);
		return status;
	}

	private static McpSession RequireLiveSession(McpContextLookup lookup)
	{
		if (lookup.Kind == McpContextLookupKind.Exited)
			throw TargetExited(lookup.State!.Session);
		return lookup.State!.Session;
	}

	private static AutomationException TargetExited(McpSession session) =>
		new(AutomationErrorCodes.TargetExited, $"Target process {session.Target.ProcessId} has exited.");

	private static McpTargetStatus UnattachedStatus() => new() { Attached = false, IsAlive = false };

	private static void ThrowIfFailedPayloadResponse(object response)
	{
		if (!TryConvertFailedPayloadResponse(response, out var standard))
			return;

		throw new AutomationException(
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
