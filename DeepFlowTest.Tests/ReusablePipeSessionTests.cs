namespace DeepFlowTest.Tests;

using System;
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

	private static string UniquePipeName()
	{
		return $"deepflowtest-test-{Guid.NewGuid():N}";
	}
}
