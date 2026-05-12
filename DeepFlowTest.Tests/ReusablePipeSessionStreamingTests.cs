namespace DeepFlowTest.Tests;

using System;
using System.IO.Pipes;
using System.Threading.Tasks;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class ReusablePipeSessionStreamingTests
{
	[TearDown]
	public void TearDown()
	{
		ReusablePipeSessionRegistry.ClearForTests();
	}

	[Test]
	public async Task StartCommandWritesFramesAndStatusSequenceAdvances()
	{
		var pipeName = UniquePipeName();
		PayloadLog.Initialize(pipeName);
		_ = ReusablePipeSessionRegistry.GetOrStart(pipeName);

		using var stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		stream.Connect(1000);
		await MessagePacker.WriteFrameAsync(stream, new StartSendingCommandRequest
		{
			StreamKind = ProtocolConstants.StreamKinds.EventLog,
			IntervalMs = 50,
		});
		var start = MessagePacker.ConvertTo<StartSendingCommandResponse>((await MessagePacker.ReadFrameAsync(stream)).Message!);
		var frame = MessagePacker.ConvertTo<StreamMessage>(
			(await TimeoutAfter(MessagePacker.ReadFrameAsync(stream), TimeSpan.FromSeconds(3))).Message!);

		using var statusClient = new NamedPipeClient(pipeName);
		var status = MessagePacker.ConvertTo<PipeStatusCommandResponse>(
			await statusClient.SendAsync(new PipeStatusCommandRequest()));

		using var stopClient = new NamedPipeClient(pipeName);
		var stop = MessagePacker.ConvertTo<StopSendingCommandResponse>(
			await stopClient.SendAsync(new StopSendingCommandRequest { SubscriptionId = start.SubscriptionId }));

		Assert.That(start.Status, Is.EqualTo(ProtocolConstants.Statuses.Started));
		Assert.That(frame.MessageKind, Is.EqualTo("stream"));
		Assert.That(frame.SequenceNumber, Is.GreaterThanOrEqualTo(1));
		Assert.That(status.IsSending, Is.True);
		Assert.That(status.ActiveSubscriptions[0].LastSequenceNumber, Is.GreaterThanOrEqualTo(1));
		Assert.That(stop.Status, Is.EqualTo(ProtocolConstants.Statuses.Stopped));
	}

	[Test]
	public void DirectSubscriptionStopAndDuplicateStopAreObservable()
	{
		var session = new ReusablePipeSession("pipe", _ => { });
		var sends = 0;
		var subscription = session.StartSubscription(
			ProtocolConstants.StreamKinds.EventLog,
			"connection",
			50,
			_ =>
			{
				sends++;
				return sends < 3;
			},
			sequence => new { sequence });

		Assert.That(SpinWaitUntil(() => sends >= 1, TimeSpan.FromSeconds(2)), Is.True);
		Assert.That(session.StopSubscription(subscription.SubscriptionId), Is.True);
		Assert.That(session.StopSubscription(subscription.SubscriptionId), Is.False);
	}

	[Test]
	public void DisconnectCancelsActiveSubscription()
	{
		var session = new ReusablePipeSession("pipe", _ => { });
		var sends = 0;
		_ = session.StartSubscription(
			ProtocolConstants.StreamKinds.EventLog,
			"connection",
			50,
			_ =>
			{
				sends++;
				return true;
			},
			sequence => new { sequence });

		Assert.That(SpinWaitUntil(() => sends >= 1, TimeSpan.FromSeconds(2)), Is.True);
		session.MarkClientDisconnected("connection");
		var countAfterDisconnect = sends;
		System.Threading.Thread.Sleep(150);

		Assert.That(session.ActiveSubscriptions, Is.Empty);
		Assert.That(sends, Is.EqualTo(countAfterDisconnect));
	}

	[Test]
	public async Task MalformedStartStopRequestsUseInvalidArguments()
	{
		var pipeName = UniquePipeName();
		PayloadLog.Initialize(pipeName);
		_ = ReusablePipeSessionRegistry.GetOrStart(pipeName);

		using var startClient = new NamedPipeClient(pipeName);
		var start = MessagePacker.ConvertTo<StandardIpcResponse>(
			await startClient.SendAsync(new StartSendingCommandRequest { StreamKind = "bad", IntervalMs = 50 }));
		using var stopClient = new NamedPipeClient(pipeName);
		var stop = MessagePacker.ConvertTo<StandardIpcResponse>(
			await stopClient.SendAsync(new StopSendingCommandRequest { SubscriptionId = "" }));

		Assert.That(start.Success, Is.False);
		Assert.That(start.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.InvalidArguments));
		Assert.That(stop.Success, Is.False);
		Assert.That(stop.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.InvalidArguments));
	}

	private static async Task<T> TimeoutAfter<T>(Task<T> task, TimeSpan timeout)
	{
		var completed = await Task.WhenAny(task, Task.Delay(timeout));
		if (completed != task)
			throw new TimeoutException();

		return await task;
	}

	private static bool SpinWaitUntil(Func<bool> condition, TimeSpan timeout)
	{
		var stop = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < stop)
		{
			if (condition())
				return true;
			System.Threading.Thread.Sleep(10);
		}

		return false;
	}

	private static string UniquePipeName()
	{
		return $"deepflowtest-stream-test-{Guid.NewGuid():N}";
	}
}
