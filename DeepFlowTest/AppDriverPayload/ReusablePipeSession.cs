namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class ReusablePipeSession
{
	private readonly Action<ReusablePipeSession> runCommandLoop;
	private readonly Dictionary<string, ActiveSubscriptionResponse> subscriptions = new();
	private int started;
	private int isBusy;

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

	public bool IsBusy => isBusy == 1;

	public int TotalCommandsHandled { get; private set; }

	public int DisconnectedClientCount { get; private set; }

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

	public void MarkClientDisconnected(string connectionId)
	{
		DisconnectedClientCount++;
		StopSubscriptionsForConnection(connectionId);
	}

	public ActiveSubscriptionResponse StartSubscription(string kind, string? connectionId)
	{
		var subscription = new ActiveSubscriptionResponse
		{
			SubscriptionId = Guid.NewGuid().ToString("N"),
			Kind = kind,
			ConnectionId = connectionId,
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
		};
	}

	private static void RunReusableCommandLoop(ReusablePipeSession session)
	{
		PayloadLog.Write($"Starting reusable command loop for pipe '{session.PipeName}'.");
		using var channel = new ReusableNamedPipeServer(session.PipeName);
		channel.ClientDisconnected += session.MarkClientDisconnected;

		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = session.PipeName,
			Mode = PayloadStartupModes.ReusableCli,
			PayloadRoot = AppContext.BaseDirectory,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};

		while (true)
		{
			NamedPipeServer.Command? command = null;
			try
			{
				command = channel.WaitForNextCommand();
				if (!command.HasValue)
					continue;

				var kind = AppDriverCommandDispatcher.GetCommandKind(command.Value.Value);
				var reportsBusy = kind != ProtocolConstants.Commands.Hello && kind != ProtocolConstants.Commands.PipeStatus;
				if (reportsBusy)
					Interlocked.Exchange(ref session.isBusy, 1);

				session.TotalCommandsHandled++;
				try
				{
					AppDriverCommandDispatcher.Process(command.Value, options, session);
				}
				finally
				{
					if (reportsBusy)
						Interlocked.Exchange(ref session.isBusy, 0);
				}
			}
			catch (Exception ex)
			{
				PayloadLog.Write("Reusable command loop recovered from a command failure.", ex);
				if (command.HasValue && !command.Value.CheckHasResponded())
					command.Value.Respond(StandardIpcResponse.FromError(ex.ToString(), ProtocolConstants.ErrorCodes.ProtocolError, LogCorrelationId()));
			}
		}
	}

	private static string LogCorrelationId()
	{
		return System.IO.Path.GetFileNameWithoutExtension(PayloadLog.CurrentLogPath);
	}
}
