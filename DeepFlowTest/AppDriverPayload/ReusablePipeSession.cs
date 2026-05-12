namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.AppDriverPayload.Streaming;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class ReusablePipeSession
{
	private readonly Action<ReusablePipeSession> runCommandLoop;
	private readonly Dictionary<string, ActiveSubscriptionState> subscriptions = new();
	private int started;
	private int busyCount;
	private int totalCommandsHandled;
	private int disconnectedClientCount;
	private volatile bool stopRequested;
	private ReusableNamedPipeServer? server;

	public ReusablePipeSession(string pipeName)
		: this(pipeName, RunReusableCommandLoop)
	{
	}

	public ReusablePipeSession(string pipeName, Action<ReusablePipeSession> runCommandLoop)
	{
		PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
		this.runCommandLoop = runCommandLoop ?? throw new ArgumentNullException(nameof(runCommandLoop));
	}

	public string PipeName { get; }

	public bool IsStarted => started == 1;

	public bool IsBusy => Volatile.Read(ref busyCount) > 0;

	public int TotalCommandsHandled => Volatile.Read(ref totalCommandsHandled);

	public int DisconnectedClientCount => Volatile.Read(ref disconnectedClientCount);

	public IReadOnlyList<ActiveSubscriptionResponse> ActiveSubscriptions
	{
		get
		{
			lock (subscriptions)
				return subscriptions.Values.Select(static subscription => subscription.ToResponse()).ToArray();
		}
	}

	public void Start()
	{
		if (Interlocked.Exchange(ref started, 1) == 1)
			return;

		var thread = new Thread(() => runCommandLoop(this))
		{
			IsBackground = true,
			Name = $"{nameof(ReusablePipeSession)}:{PipeName}",
		};
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
	}

	public void Stop()
	{
		stopRequested = true;
		StopAllSubscriptions();
		server?.Dispose();
	}

	public void MarkClientDisconnected(string connectionId)
	{
		Interlocked.Increment(ref disconnectedClientCount);
		StopSubscriptionsForConnection(connectionId);
	}

	public ActiveSubscriptionResponse StartSubscription(
		string kind,
		string? connectionId,
		int intervalMs,
		Func<object, bool> send,
		Func<long, object> capture,
		bool deferStart = false)
	{
		var subscriptionId = Guid.NewGuid().ToString("N");
		var subscription = new DelegateStreamSubscription(
			subscriptionId,
			kind,
			connectionId,
			intervalMs,
			send,
			capture);
		var state = new ActiveSubscriptionState(subscription);

		lock (subscriptions)
			subscriptions[subscription.SubscriptionId] = state;

		if (!deferStart)
			StartSubscriptionState(state);

		return state.ToResponse();
	}

	public ActiveSubscriptionResponse StartSubscription(string kind, string? connectionId, int intervalMs = 1000) =>
		StartSubscription(kind, connectionId, intervalMs, _ => false, _ => new { status = "test" }, deferStart: true);

	public bool StartStoredSubscription(string subscriptionId)
	{
		ActiveSubscriptionState? state;
		lock (subscriptions)
			subscriptions.TryGetValue(subscriptionId, out state);

		if (state is null)
			return false;

		StartSubscriptionState(state);
		return true;
	}

	public bool StopSubscription(string subscriptionId, int timeoutMs = 2000)
	{
		if (string.IsNullOrEmpty(subscriptionId))
			return false;

		ActiveSubscriptionState? state;
		lock (subscriptions)
		{
			if (!subscriptions.TryGetValue(subscriptionId, out state))
				return false;

			subscriptions.Remove(subscriptionId);
		}

		StopAndDispose(state, timeoutMs);
		if (!string.IsNullOrEmpty(state.Subscription.ConnectionId))
			server?.CloseConnection(state.Subscription.ConnectionId!);
		return true;
	}

	public void StopSubscriptionsForConnection(string connectionId)
	{
		if (string.IsNullOrEmpty(connectionId))
			return;

		List<ActiveSubscriptionState> removed;
		lock (subscriptions)
		{
			removed = subscriptions.Values
				.Where(subscription => string.Equals(subscription.Subscription.ConnectionId, connectionId, StringComparison.Ordinal))
				.ToList();
			foreach (var subscription in removed)
			{
				subscriptions.Remove(subscription.Subscription.SubscriptionId);
			}
		}

		foreach (var subscription in removed)
			StopAndDispose(subscription, timeoutMs: 500);
	}

	private void StopAllSubscriptions()
	{
		List<ActiveSubscriptionState> removed;
		lock (subscriptions)
		{
			removed = subscriptions.Values.ToList();
			subscriptions.Clear();
		}

		foreach (var subscription in removed)
			StopAndDispose(subscription, timeoutMs: 500);
	}

	public PipeStatusCommandResponse CreateStatusResponse()
	{
		var activeSubscriptions = ActiveSubscriptions;
		return new PipeStatusCommandResponse
		{
			PipeName = PipeName,
			IsReusable = true,
			IsBusy = IsBusy,
			IsSending = activeSubscriptions.Count > 0,
			ActiveSubscriptionCount = activeSubscriptions.Count,
			ActiveSubscriptions = activeSubscriptions,
			TotalCommandsHandled = TotalCommandsHandled,
			DisconnectedClientCount = DisconnectedClientCount,
			IdleMode = IsBusy ? "busy" : "waiting-for-client-or-command",
			Counters = new Dictionary<string, long>
			{
				["commandsHandled"] = TotalCommandsHandled,
				["activeSubscriptions"] = activeSubscriptions.Count,
				["disconnectedClients"] = DisconnectedClientCount,
			},
		};
	}

	private static void RunReusableCommandLoop(ReusablePipeSession session)
	{
		PayloadLog.Write($"Starting reusable command loop for pipe '{session.PipeName}'.");
		using var channel = new ReusableNamedPipeServer(session.PipeName);
		session.server = channel;
		channel.ClientDisconnected += session.MarkClientDisconnected;

		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = session.PipeName,
			Mode = PayloadStartupModes.ReusableCli,
			PayloadRoot = AppContext.BaseDirectory,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};

		while (!session.stopRequested)
		{
			NamedPipeServer.Command? command = null;
			try
			{
				command = channel.WaitForNextCommand();
				if (!command.HasValue)
					continue;

				var kind = AppDriverCommandDispatcher.GetCommandKind(command.Value.Value);
				var reportsBusy = ReportsBusy(kind);
				Interlocked.Increment(ref session.totalCommandsHandled);
				if (reportsBusy)
				{
					Interlocked.Increment(ref session.busyCount);
					var busyCommand = command.Value;
					_ = System.Threading.Tasks.Task.Run(() => ProcessCommand(session, busyCommand, options, reportsBusy: true));
					continue;
				}

				ProcessCommand(session, command.Value, options, reportsBusy: false);
			}
			catch (ObjectDisposedException) when (session.stopRequested)
			{
				break;
			}
			catch (IOException) when (session.stopRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				PayloadLog.Write("Reusable command loop recovered from a command failure.", ex);
				if (command.HasValue && !command.Value.CheckHasResponded())
					command.Value.Respond(StandardIpcResponse.FromError(ex.ToString(), ProtocolConstants.ErrorCodes.ProtocolError, PayloadLog.CurrentCorrelationId));
			}
		}

		session.server = null;
		Interlocked.Exchange(ref session.started, 0);
	}

	private static void ProcessCommand(
		ReusablePipeSession session,
		NamedPipeServer.Command command,
		AppDriverPayloadStartupOptions options,
		bool reportsBusy)
	{
		try
		{
			AppDriverCommandDispatcher.Process(command, options, session);
		}
		catch (Exception ex)
		{
			PayloadLog.Write("Reusable command processing failed.", ex);
			if (!command.CheckHasResponded())
				command.Respond(StandardIpcResponse.FromError(ex.ToString(), ProtocolConstants.ErrorCodes.ProtocolError, PayloadLog.CurrentCorrelationId));
		}
		finally
		{
			if (reportsBusy)
				Interlocked.Decrement(ref session.busyCount);
		}
	}

	private static bool ReportsBusy(string kind)
	{
		return kind is not (ProtocolConstants.Commands.Hello
			or ProtocolConstants.Commands.Ping
			or ProtocolConstants.Commands.PipeStatus
			or ProtocolConstants.Commands.StartSending
			or ProtocolConstants.Commands.StopSending);
	}

	private void StartSubscriptionState(ActiveSubscriptionState state)
	{
		state.Subscription.Start();
		if (state.Subscription.Completion is null)
			return;

		state.Subscription.Completion.ContinueWith(
			_ =>
			{
				var removed = false;
				lock (subscriptions)
				{
					if (subscriptions.TryGetValue(state.Subscription.SubscriptionId, out var current) && ReferenceEquals(current, state))
					{
						subscriptions.Remove(state.Subscription.SubscriptionId);
						removed = true;
					}
				}

				if (removed)
					state.Subscription.Dispose();
			},
			TaskScheduler.Default);
	}

	private static void StopAndDispose(ActiveSubscriptionState state, int timeoutMs)
	{
		state.Subscription.Stop();
		try
		{
			state.Subscription.Completion?.Wait(Math.Max(1, timeoutMs));
		}
		catch (AggregateException ex) when (ex.InnerExceptions.All(static inner => inner is TaskCanceledException or OperationCanceledException))
		{
		}
		finally
		{
			state.Subscription.Dispose();
		}
	}

	private sealed class ActiveSubscriptionState
	{
		public ActiveSubscriptionState(StreamSubscription subscription)
		{
			Subscription = subscription;
		}

		public StreamSubscription Subscription { get; }

		public ActiveSubscriptionResponse ToResponse() =>
			new()
			{
				SubscriptionId = Subscription.SubscriptionId,
				Kind = Subscription.StreamKind,
				ConnectionId = Subscription.ConnectionId,
				IntervalMs = Subscription.IntervalMs,
				LastSequenceNumber = Subscription.LastSequenceNumber,
			};
	}
}
