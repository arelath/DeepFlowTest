namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class ReusablePipeSession
{
	private readonly Action<ReusablePipeSession> runCommandLoop;
	private readonly Dictionary<string, ActiveSubscriptionResponse> subscriptions = new();
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
				return subscriptions.Values.ToArray();
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
		server?.Dispose();
	}

	public void MarkClientDisconnected(string connectionId)
	{
		Interlocked.Increment(ref disconnectedClientCount);
		StopSubscriptionsForConnection(connectionId);
	}

	public ActiveSubscriptionResponse StartSubscription(string kind, string? connectionId, int intervalMs = 1000)
	{
		var subscription = new ActiveSubscriptionResponse
		{
			SubscriptionId = Guid.NewGuid().ToString("N"),
			Kind = kind,
			ConnectionId = connectionId,
			IntervalMs = intervalMs,
		};

		lock (subscriptions)
			subscriptions[subscription.SubscriptionId] = subscription;

		return subscription;
	}

	public void StopSubscriptionsForConnection(string connectionId)
	{
		if (string.IsNullOrEmpty(connectionId))
			return;

		lock (subscriptions)
		{
			foreach (var subscriptionId in subscriptions.Values
				.Where(subscription => string.Equals(subscription.ConnectionId, connectionId, StringComparison.Ordinal))
				.Select(subscription => subscription.SubscriptionId)
				.ToArray())
			{
				subscriptions.Remove(subscriptionId);
			}
		}
	}

	public bool StopSubscription(string subscriptionId)
	{
		if (string.IsNullOrEmpty(subscriptionId))
			return false;

		lock (subscriptions)
			return subscriptions.Remove(subscriptionId);
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
					command.Value.Respond(StandardIpcResponse.FromError(ex.ToString(), ProtocolConstants.ErrorCodes.ProtocolError, LogCorrelationId()));
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
				command.Respond(StandardIpcResponse.FromError(ex.ToString(), ProtocolConstants.ErrorCodes.ProtocolError, LogCorrelationId()));
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

	private static string LogCorrelationId()
	{
		return System.IO.Path.GetFileNameWithoutExtension(PayloadLog.CurrentLogPath);
	}
}
