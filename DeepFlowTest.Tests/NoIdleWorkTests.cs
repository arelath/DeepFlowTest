namespace DeepFlowTest.Tests;

using System;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class NoIdleWorkTests
{
	[Test]
	public void ReusableSessionStartsIdleBeforeAnyCommand()
	{
		var session = new ReusablePipeSession("test-pipe", _ => { });

		var status = session.CreateStatusResponse();

		Assert.That(status.IsBusy, Is.False);
		Assert.That(status.IsSending, Is.False);
		Assert.That(status.ActiveSubscriptionCount, Is.EqualTo(0));
		Assert.That(status.TotalCommandsHandled, Is.EqualTo(0));
	}

	[Test]
	public void NonStreamCommandLeavesSessionIdle()
	{
		var session = new ReusablePipeSession("test-pipe", _ => { });
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");

		Dispatch(new PingCommandRequest(), session);
		var status = session.CreateStatusResponse();

		Assert.That(status.IsBusy, Is.False);
		Assert.That(status.IsSending, Is.False);
		Assert.That(status.ActiveSubscriptionCount, Is.EqualTo(0));
	}

	[Test]
	public void StreamStartAndStopReturnsSessionToIdle()
	{
		var session = new ReusablePipeSession("test-pipe", _ => { });
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");

		var start = (StartSendingCommandResponse)Dispatch(new StartSendingCommandRequest(), session)!;
		Dispatch(new StopSendingCommandRequest { SubscriptionId = start.SubscriptionId }, session);
		var status = session.CreateStatusResponse();

		Assert.That(status.IsBusy, Is.False);
		Assert.That(status.IsSending, Is.False);
		Assert.That(status.ActiveSubscriptionCount, Is.EqualTo(0));
	}

	private static object? Dispatch(object request, ReusablePipeSession session)
	{
		object? response = null;
		var command = new NamedPipeServer.Command
		{
			Value = request,
			Respond = value => response = value,
			CheckHasResponded = () => response is not null,
			HoldConnectionOpen = () => { },
		};
		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = session.PipeName,
			Mode = PayloadStartupModes.ReusableCli,
			PayloadRoot = AppContext.BaseDirectory,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};
		var dispatcherType = Type.GetType("DeepFlowTest.AppDriverPayload.AppDriverCommandDispatcher, DeepFlowTest", throwOnError: true)!;
		var method = dispatcherType.GetMethod("Process", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
		method.Invoke(null, new object?[] { command, options, session });
		return response;
	}
}
