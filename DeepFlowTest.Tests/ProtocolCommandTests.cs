namespace DeepFlowTest.Tests;

using System;
using System.Threading;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using NUnit.Framework;

[TestFixture]
public sealed class ProtocolCommandTests
{
	[Test]
	public void ProtocolDtosRoundTripThroughMessagePacker()
	{
		Assert.That(MessagePacker.ConvertTo<HelloCommandRequest>(MessagePacker.Unpack(MessagePacker.Pack(new HelloCommandRequest()))).Kind, Is.EqualTo(ProtocolConstants.Commands.Hello));
		Assert.That(MessagePacker.ConvertTo<PingCommandRequest>(MessagePacker.Unpack(MessagePacker.Pack(new PingCommandRequest()))).Kind, Is.EqualTo(ProtocolConstants.Commands.Ping));
		Assert.That(MessagePacker.ConvertTo<PipeStatusCommandRequest>(MessagePacker.Unpack(MessagePacker.Pack(new PipeStatusCommandRequest()))).Kind, Is.EqualTo(ProtocolConstants.Commands.PipeStatus));
	}

	[Test]
	public void ProtocolVersionMismatchReturnsStableError()
	{
		var pipeName = $"deepflowtest-test-{Guid.NewGuid():N}";
		PayloadLog.Initialize(pipeName);
		var session = new ReusablePipeSession(pipeName, _ => { });
		var response = CaptureResponse(new HelloCommandRequest { ProtocolVersion = "999" }, session);

		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		Assert.That(((StandardIpcResponse)response!).ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.UnsupportedProtocol));
	}

	[Test]
	public void PingReturnsCheapTargetMetadata()
	{
		var pipeName = $"deepflowtest-test-{Guid.NewGuid():N}";
		PayloadLog.Initialize(pipeName);
		var response = CaptureResponse(new PingCommandRequest(), reusableSession: null);

		Assert.That(response, Is.TypeOf<PingCommandResponse>());
		Assert.That(((PingCommandResponse)response!).ProcessId, Is.GreaterThan(0));
	}

	[Test]
	public void DirectDispatcherInvocationRunsInline()
	{
		var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
		var callerThreadId = Thread.CurrentThread.ManagedThreadId;
		var actionThreadId = 0;

		ThreadUtility.RunOnDispatcher(dispatcher, () => actionThreadId = Thread.CurrentThread.ManagedThreadId);

		Assert.That(actionThreadId, Is.EqualTo(callerThreadId));
	}

	[Test]
	public void BackgroundInvocationMarshalsToDispatcherThread()
	{
		System.Windows.Threading.Dispatcher? dispatcher = null;
		var dispatcherThreadId = 0;
		var ready = new ManualResetEventSlim();
		var thread = new Thread(() =>
		{
			dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
			dispatcherThreadId = Thread.CurrentThread.ManagedThreadId;
			ready.Set();
			System.Windows.Threading.Dispatcher.Run();
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.That(ready.Wait(TimeSpan.FromSeconds(2)), Is.True);

		var actionThreadId = 0;
		try
		{
			ThreadUtility.RunOnDispatcher(dispatcher!, () => actionThreadId = Thread.CurrentThread.ManagedThreadId);
			Assert.That(actionThreadId, Is.EqualTo(dispatcherThreadId));
		}
		finally
		{
			dispatcher!.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal);
			thread.Join(TimeSpan.FromSeconds(2));
		}
	}

	private static object? CaptureResponse(object request, ReusablePipeSession? reusableSession)
	{
		object? response = null;
		var command = new NamedPipeServer.Command
		{
			Value = request,
			Respond = value => response = value,
			CheckHasResponded = () => response is not null,
			TrySend = value =>
			{
				response = value;
				return true;
			},
		};
		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = "test-pipe",
			Mode = reusableSession is null ? PayloadStartupModes.OneShotDriver : PayloadStartupModes.ReusableCli,
			PayloadRoot = AppContext.BaseDirectory,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};

		var dispatcherType = Type.GetType("DeepFlowTest.AppDriverPayload.AppDriverCommandDispatcher, DeepFlowTest", throwOnError: true)!;
		var method = dispatcherType.GetMethod("Process", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
		method.Invoke(null, new object?[] { command, options, reusableSession });
		return response;
	}
}
