namespace DeepFlowTest.Tests;

using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class ReusablePipeSessionTests
{
	[TearDown]
	public void TearDown()
	{
		ReusablePipeSessionRegistry.ClearForTests();
	}

	[Test]
	public async Task ReusableNamedPipeServerAcceptsSequentialClients()
	{
		var pipeName = UniquePipeName();
		var serverTask = Task.Run(() =>
		{
			using var server = new ReusableNamedPipeServer(pipeName);
			for (var i = 1; i <= 2; i++)
			{
				var command = server.WaitForNextCommand();
				command!.Value.Respond(new StandardIpcResponse { Status = $"response-{i}" });
			}
		});

		using (var first = new NamedPipeClient(pipeName))
		{
			var response = MessagePacker.ConvertTo<StandardIpcResponse>(await first.SendAsync(new HelloCommandRequest()));
			Assert.That(response.Status, Is.EqualTo("response-1"));
		}

		using (var second = new NamedPipeClient(pipeName))
		{
			var response = MessagePacker.ConvertTo<StandardIpcResponse>(await second.SendAsync(new HelloCommandRequest()));
			Assert.That(response.Status, Is.EqualTo("response-2"));
		}

		await serverTask;
	}

	[Test]
	public async Task ReusableNamedPipeServerAcceptsSequentialCommandsOnOneClient()
	{
		var pipeName = UniquePipeName();
		var connectionIds = new string?[2];
		var serverTask = Task.Run(() =>
		{
			using var server = new ReusableNamedPipeServer(pipeName);
			for (var index = 0; index < 2; index++)
			{
				var command = server.WaitForNextCommand()!.Value;
				connectionIds[index] = command.ConnectionId;
				command.Respond(new StandardIpcResponse { Status = $"response-{index + 1}" });
			}
		});

		using var client = new NamedPipeClient(pipeName);
		var first = MessagePacker.ConvertTo<StandardIpcResponse>(await client.SendAsync(new HelloCommandRequest()));
		var second = MessagePacker.ConvertTo<StandardIpcResponse>(await client.SendAsync(new PingCommandRequest()));

		Assert.That(first.Status, Is.EqualTo("response-1"));
		Assert.That(second.Status, Is.EqualTo("response-2"));
		await serverTask;
		Assert.That(connectionIds[0], Is.Not.Empty.And.EqualTo(connectionIds[1]));
	}

	[Test]
	public void DuplicateSessionStartupReturnsExistingSession()
	{
		var started = 0;
		var first = ReusablePipeSessionRegistry.GetOrStart("same-pipe", _ => Interlocked.Increment(ref started));
		var second = ReusablePipeSessionRegistry.GetOrStart("same-pipe", _ => Interlocked.Increment(ref started));

		Assert.That(second, Is.SameAs(first));
		Assert.That(ReusablePipeSessionRegistry.Count, Is.EqualTo(1));
		Assert.That(SpinWait.SpinUntil(() => started == 1, TimeSpan.FromSeconds(2)), Is.True);
	}

	[Test]
	public void ClientDisconnectCleansConnectionScopedState()
	{
		var session = new ReusablePipeSession("pipe", _ => { });
		var subscription = session.StartSubscription("visual-tree", "connection-1");

		session.MarkClientDisconnected("connection-1");

		Assert.That(session.DisconnectedClientCount, Is.EqualTo(1));
		Assert.That(session.ActiveSubscriptions, Is.Empty);
		Assert.That(subscription.SubscriptionId, Is.Not.Empty);
	}

	[Test]
	public async Task ReusableSessionAnswersHelloAndReturnsIdleStatus()
	{
		var pipeName = UniquePipeName();
		PayloadLog.Initialize(pipeName);
		_ = ReusablePipeSessionRegistry.GetOrStart(pipeName);

		using (var helloClient = new NamedPipeClient(pipeName))
		{
			var hello = MessagePacker.ConvertTo<HelloCommandResponse>(await helloClient.SendAsync(new HelloCommandRequest()));
			Assert.That(hello.PipeName, Is.EqualTo(pipeName));
			Assert.That(hello.IsReusable, Is.True);
			Assert.That(hello.ConnectionId, Is.Not.Empty);
			Assert.That(hello.ControlConnectionMode, Is.EqualTo(ProtocolConstants.ControlConnectionModes.PersistentSerialized));
		}

		using (var statusClient = new NamedPipeClient(pipeName))
		{
			var status = MessagePacker.ConvertTo<PipeStatusCommandResponse>(await statusClient.SendAsync(new PipeStatusCommandRequest()));
			Assert.That(status.IsReusable, Is.True);
			Assert.That(status.IsBusy, Is.False);
			Assert.That(status.IdleMode, Is.EqualTo("waiting-for-client-or-command"));
			Assert.That(status.TotalCommandsHandled, Is.GreaterThanOrEqualTo(2));
		}
	}

	[Test]
	public async Task ReusableSessionAnswersStatusWhileStreamingConnectionIsHeld()
	{
		var pipeName = UniquePipeName();
		PayloadLog.Initialize(pipeName);
		_ = ReusablePipeSessionRegistry.GetOrStart(pipeName);

		using var streamClient = new NamedPipeClient(pipeName);
		var start = MessagePacker.ConvertTo<StartSendingCommandResponse>(
			await streamClient.SendAsync(new StartSendingCommandRequest
			{
				StreamKind = ProtocolConstants.StreamKinds.VisualTree,
				IntervalMs = 1000,
			}));

		using var statusClient = new NamedPipeClient(pipeName);
		var status = MessagePacker.ConvertTo<PipeStatusCommandResponse>(await statusClient.SendAsync(new PipeStatusCommandRequest()));

		Assert.That(start.Status, Is.EqualTo(ProtocolConstants.Statuses.Started));
		Assert.That(status.IsSending, Is.True);
		Assert.That(status.ActiveSubscriptionCount, Is.EqualTo(1));
		Assert.That(status.ActiveConnectionCount, Is.GreaterThanOrEqualTo(2));
		Assert.That(status.Counters["activeConnections"], Is.GreaterThanOrEqualTo(2));
	}

	[Test]
	public async Task ReusableSessionAnswersStatusWhileCommandIsBusy()
	{
		var pipeName = UniquePipeName();
		PayloadLog.Initialize(pipeName);
		var session = ReusablePipeSessionRegistry.GetOrStart(pipeName);
		using var _ = DelayUiHandlers(500);

		var busyTask = Task.Run(async () =>
		{
			using var busyClient = new NamedPipeClient(pipeName);
			return await busyClient.SendAsync(new GetVisualTreeCommandRequest { TimeoutMs = 1000 }, responseTimeoutMs: 2000);
		});

		Assert.That(SpinWait.SpinUntil(() => session.IsBusy, TimeSpan.FromSeconds(2)), Is.True);
		using var statusClient = new NamedPipeClient(pipeName);
		var status = MessagePacker.ConvertTo<PipeStatusCommandResponse>(await statusClient.SendAsync(new PipeStatusCommandRequest()));

		Assert.That(status.IsBusy, Is.True);
		await busyTask;
	}

	[Test]
	public async Task ReusableServerReportsRealClientDisconnectBeforeCommand()
	{
		var pipeName = UniquePipeName();
		var disconnected = new TaskCompletionSource<string>();
		var serverTask = Task.Run(() =>
		{
			using var server = new ReusableNamedPipeServer(pipeName);
			server.ClientDisconnected += connectionId => disconnected.TrySetResult(connectionId);
			_ = server.WaitForNextCommand();
		});

		using (var rawClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
			rawClient.Connect(1000);

		var completed = await Task.WhenAny(disconnected.Task, Task.Delay(TimeSpan.FromSeconds(2)));

		Assert.That(completed, Is.SameAs(disconnected.Task));
		Assert.That(disconnected.Task.Result, Is.Not.Empty);
		await serverTask;
	}

	private static string UniquePipeName()
	{
		return $"deepflowtest-test-{Guid.NewGuid():N}";
	}

	private static IDisposable DelayUiHandlers(int delayMs)
	{
		return AppDriverCommandDispatcher.DelayUiHandlersForTests(delayMs);
	}
}
