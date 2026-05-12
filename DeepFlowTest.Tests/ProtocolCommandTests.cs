namespace DeepFlowTest.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
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
		foreach (var command in CreateAllProtocolCommandDtos())
		{
			var roundTripped = MessagePacker.ConvertTo(command, command.GetType());

			Assert.That(((IpcCommand)roundTripped).Kind, Is.EqualTo(command.Kind));
		}
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
	public void UnknownCommandReturnsProtocolError()
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");

		var response = CaptureResponse(new UnknownCommand(), reusableSession: null);

		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		Assert.That(((StandardIpcResponse)response!).ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.ProtocolError));
	}

	[Test]
	public void EveryKnownCommandProducesExactlyOneResponse()
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");
		var session = new ReusablePipeSession("test-pipe", _ => { });

		foreach (var request in CreateAllProtocolCommandDtos())
		{
			var capture = CaptureDispatch(request, session);

			Assert.That(capture.ResponseCount, Is.EqualTo(1), request.Kind);
			Assert.That(capture.Response, Is.Not.Null, request.Kind);
		}
	}

	[Test]
	public void StreamingStartStopUpdatesReusableSessionStatus()
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");
		var session = new ReusablePipeSession("test-pipe", _ => { });

		var start = (StartSendingCommandResponse)CaptureResponse(new StartSendingCommandRequest { StreamKind = ProtocolConstants.StreamKinds.VisualTree }, session)!;
		var statusAfterStart = session.CreateStatusResponse();
		var stop = (StopSendingCommandResponse)CaptureResponse(new StopSendingCommandRequest { SubscriptionId = start.SubscriptionId }, session)!;
		var statusAfterStop = session.CreateStatusResponse();

		Assert.That(start.Status, Is.EqualTo(ProtocolConstants.Statuses.Started));
		Assert.That(statusAfterStart.IsSending, Is.True);
		Assert.That(statusAfterStart.ActiveSubscriptionCount, Is.EqualTo(1));
		Assert.That(stop.Status, Is.EqualTo(ProtocolConstants.Statuses.Stopped));
		Assert.That(statusAfterStop.IsSending, Is.False);
		Assert.That(statusAfterStop.ActiveSubscriptionCount, Is.EqualTo(0));
	}

	[Test]
	public void UiCommandWithoutWpfUsesStableNativeFallbackShape()
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");

		var response = CaptureResponse(new GetVisualTreeCommandRequest(), reusableSession: null);

		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		var error = (StandardIpcResponse)response!;
		Assert.That(error.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.UnsupportedTarget));
		Assert.That(error.Error, Does.Contain("Native fallback"));
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

	[Test]
	public async Task CommandTimeoutReturnsStableResponse()
	{
		var response = await ThreadUtility.RunCommandWithTimeoutAsync(
			async () =>
			{
				await Task.Delay(500);
				return StandardIpcResponse.Ok();
			},
			timeoutMs: 10);

		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		Assert.That(((StandardIpcResponse)response).ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.CommandTimeout));
	}

	[Test]
	public async Task CommandExceptionReturnsStableResponseAndLogs()
	{
		var logged = false;
		var response = await ThreadUtility.RunCommandWithTimeoutAsync(
			() => throw new InvalidOperationException("boom"),
			timeoutMs: 100,
			log: (_, _) => logged = true,
			logCorrelationId: "log-id");

		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		var error = (StandardIpcResponse)response;
		Assert.That(error.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.ProtocolError));
		Assert.That(error.Error, Does.Contain("boom"));
		Assert.That(error.LogCorrelationId, Is.EqualTo("log-id"));
		Assert.That(logged, Is.True);
	}

	private static object? CaptureResponse(object request, ReusablePipeSession? reusableSession)
	{
		return CaptureDispatch(request, reusableSession).Response;
	}

	private static DispatchCapture CaptureDispatch(object request, ReusablePipeSession? reusableSession)
	{
		object? response = null;
		var responseCount = 0;
		var command = new NamedPipeServer.Command
		{
			Value = request,
			Respond = value =>
			{
				response = value;
				responseCount++;
			},
			CheckHasResponded = () => responseCount != 0,
			HoldConnectionOpen = () => { },
			TrySend = value =>
			{
				response = value;
				responseCount++;
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
		return new DispatchCapture(response, responseCount);
	}

	private static IpcCommand[] CreateAllProtocolCommandDtos()
	{
		return new IpcCommand[]
		{
			new HelloCommandRequest(),
			new PingCommandRequest(),
			new PipeStatusCommandRequest(),
			new StartSendingCommandRequest { StreamKind = ProtocolConstants.StreamKinds.VisualTree },
			new StopSendingCommandRequest { SubscriptionId = "missing" },
			new GetVisualTreeCommandRequest(),
			new FindElementCommandRequest { MatcherCode = "matcher" },
			new ScreenshotCommandRequest(),
			new ClickCommandRequest { TargetId = "target" },
			new FocusCommandRequest { TargetId = "target" },
			new TypeTextCommandRequest { Text = "hello" },
			new KeyPressCommandRequest { Keys = "Enter" },
			new SetPropertyCommandRequest { TargetId = "target", PropertyName = "Text", PropertyValue = "hello" },
			new RaiseEventCommandRequest { TargetId = "target", GetRoutedEventArgs = "args" },
			new KnownRoutedEventCommandRequest { TargetId = "target", EventName = "Click" },
			new KnownOperationCommandRequest { TargetId = "target", Operation = "Focus" },
			new InvokeCommandRequest { TargetId = "target", Code = "return null;" },
		};
	}

	private sealed class UnknownCommand
	{
		public string Kind { get; set; } = "UnknownCommand";
	}

	private sealed record DispatchCapture(object? Response, int ResponseCount);
}
