namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.AppDriverPayload.Diagnostics;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using NUnit.Framework;
using static DeepFlowTest.Tests.TestIpcHost;

[TestFixture]
public sealed class ProtocolCommandTests
{
	[Test]
	public void ProtocolDtosRoundTripThroughMessagePacker()
	{
		foreach (var command in CreateAllProtocolCommandDtos())
		{
			var roundTripped = MessagePacker.ConvertTo(MessagePacker.Unpack(MessagePacker.Pack(command)), command.GetType());

			Assert.That(((IpcCommand)roundTripped).Kind, Is.EqualTo(command.Kind));
		}
	}

	[Test]
	public void ContractDtosExposeCompatConstructorsAliasesAndEquality()
	{
		var leftClick = new ClickCommandRequest("button", "Left", 123);
		var sameLeftClick = new ClickCommandRequest("button", "Left", 123);
		var drag = new DragAndDropCommandRequest("source", "destination", 456);
		var screenshot = new ScreenshotCommandResponse("AQID");
		var find = new FindElementCommandResponse(new[]
		{
			new System.Collections.Generic.Dictionary<string, object?>
			{
				["TargetId"] = "button",
				["TypeName"] = "Button",
				["Name"] = "Run",
			},
		});
		var ping = new PingCommandResponse(2, 5);
		var stale = StandardIpcResponse.StaleElement();
		var stream = new StreamMessage("subscription", ProtocolConstants.StreamKinds.VisualTree, 7, new { ok = true });

		Assert.That(leftClick, Is.EqualTo(sameLeftClick));
		Assert.That(leftClick.TargetId, Is.EqualTo("button"));
		Assert.That(leftClick.MouseButton, Is.EqualTo(MouseButtonKind.Left));
		Assert.That(leftClick.TimeoutMs, Is.EqualTo(123));
		Assert.That(drag.TargetId, Is.EqualTo("source"));
		Assert.That(drag.DestinationTargetId, Is.EqualTo("destination"));
		Assert.That(drag.TimeoutMs, Is.EqualTo(456));
		Assert.That(screenshot.BytesBase64, Is.EqualTo("AQID"));
		Assert.That(screenshot.Base64Screenshot, Is.EqualTo("AQID"));
		Assert.That(find.Matches.Single().TargetId, Is.EqualTo("button"));
		Assert.That(find.Nodes.Single()["Name"], Is.EqualTo("Run"));
		Assert.That(ping.RootCount, Is.EqualTo(2));
		Assert.That(ping.NodeCount, Is.EqualTo(5));
		Assert.That(stale.Status, Is.EqualTo(ProtocolConstants.Statuses.StaleElement));
		Assert.That(stream.Kind, Is.EqualTo(ProtocolConstants.StreamKinds.VisualTree));
		Assert.That(stream.Sequence, Is.EqualTo(7));
		Assert.That(new ConfigureDiagnosticsCommandRequest
		{
			VirtualPointer = new VirtualPointerOptionsDto { Enabled = true, HideDelayMs = 123 },
		}.VirtualPointer!.HideDelayMs, Is.EqualTo(123));
		Assert.That(ProtocolConstants.Properties.Base64Screenshot, Is.EqualTo("Base64Screenshot"));
		Assert.That(ProtocolConstants.Properties.MaxMatches, Is.EqualTo("MaxMatches"));
	}

	[Test]
	public void ProtocolEnumsConvertLegacyStringsToTypedValues()
	{
		var click = MessagePacker.ConvertTo<ClickCommandRequest>(new Dictionary<string, object?>
		{
			["Kind"] = ProtocolConstants.Commands.Click,
			["TargetId"] = "button",
			["MouseButton"] = "right",
		});
		var screenshot = MessagePacker.ConvertTo<ScreenshotCommandRequest>(new Dictionary<string, object?>
		{
			["Kind"] = ProtocolConstants.Commands.Screenshot,
			["Format"] = "jpg",
		});

		Assert.That(click.MouseButton, Is.EqualTo(MouseButtonKind.Right));
		Assert.That(screenshot.Format, Is.EqualTo(ImageFormat.Jpeg));
		Assert.That(click.ToDictionary()["MouseButton"], Is.EqualTo("right"));
		Assert.That(screenshot.ToDictionary()["Format"], Is.EqualTo("jpeg"));
	}

	[Test]
	public void BindingFailureDtosRoundTripWithProtocolSeverityStrings()
	{
		var failure = new BindingFailureDto
		{
			SequenceNumber = 5,
			TimestampUtc = DateTimeOffset.UtcNow,
			Severity = BindingFailureSeverity.Error,
			Message = "System.Windows.Data Error: 40",
			RawMessage = "System.Windows.Data Error: 40",
			Source = "System.Windows.Data",
			EventId = 40,
			ManagedThreadId = Environment.CurrentManagedThreadId,
		};

		var unpacked = MessagePacker.ConvertTo<BindingFailureDto>(MessagePacker.Unpack(MessagePacker.Pack(failure)));

		Assert.That(unpacked.Severity, Is.EqualTo(BindingFailureSeverity.Error));
		Assert.That(ProtocolValueMapper.FormatBindingFailureSeverity(unpacked.Severity), Is.EqualTo("error"));
	}

	[Test]
	public void SemanticRecordingDtosRoundTripThroughMessagePacker()
	{
		var batch = new SemanticRecordingBatch
		{
			RecordingId = "recording",
			BatchSequenceNumber = 3,
			DroppedActionCount = 2,
			Frames =
			[
				new SemanticRecordingFrame
				{
					RecordingId = "recording",
					FrameKind = "action",
					SequenceNumber = 9,
					Action = new RecordedInputAction
					{
						ActionKind = "type",
						Text = "hello",
						Target = new RecordedTarget
						{
							TargetId = "target",
							TypeName = "TextBox",
							Summary = "TextBox[Name='User']",
							SelectorHints =
							[
								new RecordedSelectorHint
								{
									Kind = "name",
									Confidence = 0.85,
									PropertyName = KnownProperties.Name,
									Value = "User",
									Cli = "--name \"User\"",
								},
							],
						},
					},
				},
			],
		};

		var unpacked = MessagePacker.ConvertTo<SemanticRecordingBatch>(MessagePacker.Unpack(MessagePacker.Pack(batch)));

		Assert.That(unpacked.RecordingId, Is.EqualTo("recording"));
		Assert.That(unpacked.DroppedActionCount, Is.EqualTo(2));
		Assert.That(unpacked.Frames.Single().Action!.Text, Is.EqualTo("hello"));
		Assert.That(unpacked.Frames.Single().Action!.Target.SelectorHints.Single().Kind, Is.EqualTo("name"));
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
		var availability = ThreadUtility.GetAvailability();
		var response = CaptureResponse(new PingCommandRequest(), reusableSession: null);

		Assert.That(response, Is.TypeOf<PingCommandResponse>());
		var ping = (PingCommandResponse)response!;
		Assert.That(ping.ProcessId, Is.GreaterThan(0));
		Assert.That(ping.IsNativeFallbackAvailable, Is.EqualTo(availability.IsNativeFallbackAvailable));
	}

	[Test]
	public void OneShotHelloReturnsEnvironmentMetadata()
	{
		var pipeName = $"deepflowtest-test-{Guid.NewGuid():N}";
		PayloadLog.Initialize(pipeName);

		var response = CaptureResponse(new HelloCommandRequest(), reusableSession: null, pipeName);

		Assert.That(response, Is.TypeOf<HelloCommandResponse>());
		var hello = (HelloCommandResponse)response!;
		Assert.That(hello.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));
		Assert.That(hello.PipeName, Is.EqualTo(pipeName));
		Assert.That(hello.IsReusable, Is.False);
		Assert.That(hello.ProcessId, Is.GreaterThan(0));
		Assert.That(hello.ProcessArchitecture, Is.Not.Empty);
		Assert.That(hello.FrameworkFamily, Is.Not.Empty);
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
	public void CommandHandlersAreDiscoveredForEveryProtocolCommandDto()
	{
		var registry = CommandHandlerRegistry.CreateDefault();
		var handlersByKind = registry.AllHandlers.ToLookup(static handler => handler.Kind, StringComparer.Ordinal);

		foreach (var request in CreateAllProtocolCommandDtos())
		{
			Assert.That(handlersByKind[request.Kind].Count(), Is.EqualTo(1), request.GetType().Name);
			Assert.That(handlersByKind[request.Kind].Single().RequestType, Is.EqualTo(request.GetType()), request.Kind);
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
	public void BindingFailureCommandReturnsRecordedBatch()
	{
		BindingFailureCaptureService.Instance.ResetForTests();
		using var _ = BindingFailureCaptureService.Instance.Start(new BindingFailureCaptureSettings());
		BindingFailureCaptureService.Instance.Record(BindingFailureSeverity.Error, "System.Windows.Data Error: missing property", "test", 40);

		var response = CaptureResponse(new GetBindingFailuresCommandRequest(0));

		Assert.That(response, Is.TypeOf<BindingFailureBatchDto>());
		var batch = (BindingFailureBatchDto)response!;
		Assert.That(batch.Failures.Single().Message, Does.Contain("missing property"));
		Assert.That(batch.LastSequenceNumber, Is.EqualTo(1));
	}

	[Test]
	public void ConfigureDiagnosticsRejectsInvalidVirtualPointerOptions()
	{
		var response = CaptureResponse(new ConfigureDiagnosticsCommandRequest
		{
			VirtualPointer = new VirtualPointerOptionsDto
			{
				Enabled = true,
				HideDelayMs = -1,
			},
		});

		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		Assert.That(((StandardIpcResponse)response!).ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.InvalidArguments));
	}

	[Test]
	public void BindingFailureStreamStartsWithoutDispatcherAndStopsCaptureRegistration()
	{
		BindingFailureCaptureService.Instance.ResetForTests();
		var session = new ReusablePipeSession("test-pipe", _ => { });

		var start = (StartSendingCommandResponse)CaptureResponse(
			new StartSendingCommandRequest { StreamKind = ProtocolConstants.StreamKinds.BindingFailures, IntervalMs = 50 },
			session)!;

		Assert.That(start.StreamKind, Is.EqualTo(ProtocolConstants.StreamKinds.BindingFailures));
		Assert.That(BindingFailureCaptureService.Instance.ActiveRegistrationCount, Is.EqualTo(1));

		var stop = (StopSendingCommandResponse)CaptureResponse(new StopSendingCommandRequest { SubscriptionId = start.SubscriptionId }, session)!;

		Assert.That(stop.Status, Is.EqualTo(ProtocolConstants.Statuses.Stopped));
		Assert.That(BindingFailureCaptureService.Instance.ActiveRegistrationCount, Is.EqualTo(0));
	}

	[Test]
	public void BindingFailureStreamRejectsTargetId()
	{
		var session = new ReusablePipeSession("test-pipe", _ => { });

		var response = CaptureResponse(
			new StartSendingCommandRequest
			{
				StreamKind = ProtocolConstants.StreamKinds.BindingFailures,
				IntervalMs = 50,
				TargetId = "button",
			},
			session);

		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		Assert.That(((StandardIpcResponse)response!).ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.InvalidArguments));
	}

	[Test]
	public void SemanticRecordingStreamRejectsInvalidOptionsBeforeUiResolution()
	{
		var session = new ReusablePipeSession("test-pipe", _ => { });

		var response = CaptureResponse(
			new StartSendingCommandRequest
			{
				StreamKind = ProtocolConstants.StreamKinds.SemanticRecording,
				IntervalMs = 50,
				SemanticRecording = new SemanticRecordingOptionsDto
				{
					MaxBatchFrames = 0,
				},
			},
			session);

		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		Assert.That(((StandardIpcResponse)response!).ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.InvalidArguments));
	}

	[Test]
	public void UiCommandWithoutWpfUsesStableNativeFallbackShape()
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");

		var response = CaptureResponse(new GetVisualTreeCommandRequest(), reusableSession: null);

		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		var error = (StandardIpcResponse)response!;
		Assert.That(error.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.UnsupportedTarget));
		Assert.That(error.Error, Does.Contain("NativeFallback"));
	}

	[Test]
	public void DispatcherTimeoutReturnsSingleStableResponse()
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");
		using var _ = DelayUiHandlers(250);

		var capture = CaptureDispatch(new GetVisualTreeCommandRequest { TimeoutMs = 10 }, reusableSession: null);

		Assert.That(capture.ResponseCount, Is.EqualTo(1));
		Assert.That(capture.Response, Is.TypeOf<StandardIpcResponse>());
		Assert.That(((StandardIpcResponse)capture.Response!).ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.CommandTimeout));
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

	private static IDisposable DelayUiHandlers(int delayMs)
	{
		return AppDriverCommandDispatcher.DelayUiHandlersForTests(delayMs);
	}

	private static IpcCommand[] CreateAllProtocolCommandDtos()
	{
		return
		[
			new HelloCommandRequest(),
			new PingCommandRequest(),
			new PipeStatusCommandRequest(),
			new ConfigureDiagnosticsCommandRequest { VirtualPointer = new VirtualPointerOptionsDto() },
			new GetBindingFailuresCommandRequest(),
			new StartSendingCommandRequest { StreamKind = ProtocolConstants.StreamKinds.VisualTree },
			new StopSendingCommandRequest { SubscriptionId = "missing" },
			new GetVisualTreeCommandRequest(),
			new FindElementCommandRequest { MatcherCode = "matcher" },
			new ScreenshotCommandRequest(),
			new ClickCommandRequest { TargetId = "target" },
			new DragAndDropCommandRequest { TargetId = "source", DestinationTargetId = "destination" },
			new FocusCommandRequest { TargetId = "target" },
			new TypeTextCommandRequest { Text = "hello" },
			new KeyPressCommandRequest { Keys = "Enter" },
			new SetPropertyCommandRequest { TargetId = "target", PropertyName = "Text", PropertyValue = "hello" },
			new RaiseEventCommandRequest { TargetId = "target", GetRoutedEventArgs = "args" },
			new KnownRoutedEventCommandRequest { TargetId = "target", EventName = "Click" },
			new KnownOperationCommandRequest { TargetId = "target", Operation = "Focus" },
			new InvokeCommandRequest { TargetId = "target", Code = "return null;" },
		];
	}

	private sealed class UnknownCommand
	{
		public string Kind { get; set; } = "UnknownCommand";
	}

}
