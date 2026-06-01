namespace DeepFlowTest.Mcp.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.Prompts;
using DeepFlowTest.Mcp.Resources;
using DeepFlowTest.Mcp.Tools;
using DeepFlowTest.Mcp.ViewModels;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class McpToolTests
{
	[Test]
	public void ListProcessesFiltersCandidates()
	{
		var fixture = McpTestHost.CreateHost();

		var response = TargetTools.ListProcesses(fixture.Runner, fixture.Services);
		var data = (DeepFlowTest.Cli.ProcessListData)response.Data!;

		Assert.That(response.Success, Is.True);
		Assert.That(data.Processes.Select(static process => process.ProcessName), Is.EqualTo(new[] { "UiApp" }));
	}

	[Test]
	public void ListProcessesCandidatesOnlyKeepsWinFormsCandidates()
	{
		var snapshotSource = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes =
				[
					Process(1, "NativeWindow", isCandidate: false),
					Process(2, "WinFormsApp", isCandidate: true, frameworkFamily: "winforms"),
				],
			},
		};
		var fixture = McpTestHost.CreateHost(snapshotSource: snapshotSource);

		var response = TargetTools.ListProcesses(fixture.Runner, fixture.Services, candidatesOnly: true);
		var data = (ProcessListData)response.Data!;

		Assert.That(response.Success, Is.True);
		Assert.That(data.Processes.Select(static process => process.ProcessName), Is.EqualTo(new[] { "WinFormsApp" }));
	}

	[Test]
	public void ListProcessesCandidatesOnlyOmitsWarnedProcessesAndWarnings()
	{
		var snapshotSource = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes =
				[
					Process(1, "CleanUi", isCandidate: true),
					Process(2, "DeniedUi", isCandidate: true),
					Process(3, "Worker", isCandidate: false),
					Process(4, "WindowlessWpfBackground", isCandidate: true, hasWindow: false),
				],
				Warnings =
				[
					new ProcessInspectionWarning { ProcessId = 2, ProcessName = "DeniedUi", Message = "Access is denied." },
					new ProcessInspectionWarning { ProcessId = 5, ProcessName = "System", Message = "Access is denied." },
					new ProcessInspectionWarning { ProcessName = "Unknown", Message = "Unable to enumerate the process modules." },
				],
			},
		};
		var fixture = McpTestHost.CreateHost(snapshotSource: snapshotSource);

		var response = TargetTools.ListProcesses(fixture.Runner, fixture.Services, candidatesOnly: true);
		var data = (ProcessListData)response.Data!;

		Assert.That(response.Success, Is.True);
		Assert.That(data.Processes.Select(static process => process.ProcessName), Is.EqualTo(new[] { "CleanUi" }));
		Assert.That(data.Warnings, Is.Empty);
	}

	[Test]
	public void ListProcessesCandidatesOnlyOmitsWindowlessWpfProcesses()
	{
		var snapshotSource = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes =
				[
					Process(1, "VisibleWpf", isCandidate: true),
					Process(2, "PowerToys.PowerLauncher", isCandidate: true, hasWindow: false),
				],
			},
		};
		var fixture = McpTestHost.CreateHost(snapshotSource: snapshotSource);

		var response = TargetTools.ListProcesses(fixture.Runner, fixture.Services, candidatesOnly: true);
		var data = (ProcessListData)response.Data!;

		Assert.That(response.Success, Is.True);
		Assert.That(data.Processes.Select(static process => process.ProcessName), Is.EqualTo(new[] { "VisibleWpf" }));
	}

	[Test]
	public void ListProcessesShowAllKeepsWindowlessWpfProcessesForManualAttach()
	{
		var snapshotSource = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes =
				[
					Process(1, "VisibleWpf", isCandidate: true),
					Process(2, "PowerToys.PowerLauncher", isCandidate: true, hasWindow: false),
				],
			},
		};
		var fixture = McpTestHost.CreateHost(snapshotSource: snapshotSource);

		var response = TargetTools.ListProcesses(fixture.Runner, fixture.Services, candidatesOnly: false);
		var data = (ProcessListData)response.Data!;

		Assert.That(response.Success, Is.True);
		Assert.That(data.Processes.Select(static process => process.ProcessName), Is.EquivalentTo(new[] { "VisibleWpf", "PowerToys.PowerLauncher" }));
	}

	[Test]
	public void GetVisualTreeUsesAttachedSession()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options);

		Assert.That(response.Success, Is.True);
		var recording = (McpCondensedRecordingOutput)response.Data!;
		Assert.That(recording.Format, Is.EqualTo("condensed-agent"));
		Assert.That(recording.Text, Does.Contain("dft-condensed/1"));
		Assert.That(recording.Text, Does.Contain("SubmitButton"));
		Assert.That(sessionService.Session.Commands.OfType<GetVisualTreeCommandRequest>().Single().AsSnapshot, Is.True);
	}

	[Test]
	public void GetVisualTreeCanReturnJsonWhenRequested()
	{
		var sessionService = new FakeAppSessionService();
		sessionService.Session.Snapshot = VisualTreeSnapshot.Create(1, new[]
		{
			Node("window-0001", type: "Window", automationId: "MainWindow", childIds: ["grid-0002"], isRoot: true),
			Node("grid-0002", type: "Grid", name: "LayoutGrid", parentId: "window-0001", childIds: ["button-0003"]),
			Node("button-0003", type: "Button", automationId: "SubmitButton", text: "Submit", parentId: "grid-0002"),
		});
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = InspectTools.GetVisualTree(
			fixture.Runner,
			fixture.Host,
			fixture.Cache,
			fixture.Resources,
			fixture.Options,
			properties: "Name,AutomationId",
			outputFormat: "json");
		var data = (DeepFlowTest.Cli.TreeSnapshotData)response.Data!;

		Assert.That(response.Success, Is.True);
		Assert.That(response.Data, Is.TypeOf<DeepFlowTest.Cli.TreeSnapshotData>());
		Assert.That(data.Nodes.Select(static node => node.TypeName), Does.Contain("Grid"));
		Assert.That(data.Nodes.Single(static node => node.TargetId == "grid-0002").Properties[KnownProperties.Name], Is.EqualTo("LayoutGrid"));
	}

	[Test]
	public void GetVisualTreeCondensedOutputAppliesMcpSemanticPruning()
	{
		var sessionService = new FakeAppSessionService();
		sessionService.Session.Snapshot = VisualTreeSnapshot.Create(1, new[]
		{
			Node("window-0001", type: "Window", automationId: "MainWindow", childIds: ["grid-0002"], isRoot: true),
			Node("grid-0002", type: "Grid", name: "LayoutGrid", parentId: "window-0001", childIds: ["button-0003", "canvas-0004"]),
			Node("button-0003", type: "Button", automationId: "SubmitButton", text: "Submit", parentId: "grid-0002"),
			Node("canvas-0004", type: "Canvas", automationId: "SemanticCanvas", parentId: "grid-0002"),
		});
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options);
		var recording = (McpCondensedRecordingOutput)response.Data!;

		Assert.That(response.Success, Is.True);
		Assert.That(recording.SemanticPruning, Is.True);
		Assert.That(recording.Text, Does.Contain("Window [0001] #MainWindow"));
		Assert.That(recording.Text, Does.Contain("Button [0003] #SubmitButton"));
		Assert.That(recording.Text, Does.Contain("Canvas [0004] #SemanticCanvas"));
		Assert.That(recording.Text, Does.Not.Contain("Grid [0002]"));
		Assert.That(recording.Text, Does.Not.Contain("LayoutGrid"));
	}

	[Test]
	public void FailedIpcResponsesMapToStructuredMcpErrors()
	{
		var sessionService = new FakeAppSessionService();
		sessionService.Session.SendHandler = _ => StandardIpcResponse.FromError(
			"target went stale",
			ProtocolConstants.ErrorCodes.StaleTarget);
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = TargetTools.PingTarget(fixture.Runner, fixture.Host, fixture.Options);

		Assert.That(response.Success, Is.False);
		Assert.That(response.Error!.Code, Is.EqualTo(DeepFlowTest.Cli.CliErrorCodes.StaleTarget));
		Assert.That(response.Error.Message, Is.EqualTo("target went stale"));
		Assert.That(response.Recovery, Does.Contain("Refresh the visual tree"));
	}

	[Test]
	public void NamedPipeFailuresMapToStructuredMcpErrors()
	{
		var sessionService = new FakeAppSessionService();
		sessionService.Session.SendHandler = _ => throw new NamedPipeSessionException(
			ProtocolConstants.ErrorCodes.CommandTimeout,
			"target did not answer");
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = TargetTools.PingTarget(fixture.Runner, fixture.Host, fixture.Options);

		Assert.That(response.Success, Is.False);
		Assert.That(response.Error!.Code, Is.EqualTo(DeepFlowTest.Cli.CliErrorCodes.CommandTimeout));
		Assert.That(response.Error.Message, Is.EqualTo("target did not answer"));
		Assert.That(response.Recovery, Does.Contain("Increase timeoutMs"));
	}

	[Test]
	public void SuggestSelectorsTimeoutReturnsStructuredFailureAndDoesNotPoisonSession()
	{
		var sessionService = new FakeAppSessionService();
		sessionService.Session.SendHandler = command => command switch
		{
			GetVisualTreeCommandRequest => throw new NamedPipeSessionException(
				ProtocolConstants.ErrorCodes.CommandTimeout,
				"Command timed out after 10000 ms."),
			PingCommandRequest => new PingCommandResponse(1234, 1),
			_ => new StandardIpcResponse { Success = true },
		};
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var failed = InspectTools.SuggestSelectors(
			fixture.Runner,
			fixture.Host,
			fixture.Cache,
			fixture.Options,
			targetId: "button-0002",
			refresh: true);
		var ping = TargetTools.PingTarget(fixture.Runner, fixture.Host, fixture.Options);

		Assert.That(failed.Success, Is.False);
		Assert.That(failed.Error!.Code, Is.EqualTo(DeepFlowTest.Cli.CliErrorCodes.CommandTimeout));
		Assert.That(failed.Error.Message, Does.Contain("10000"));
		Assert.That(failed.Recovery, Does.Contain("Increase timeoutMs"));
		Assert.That(ping.Success, Is.True);
	}

	[Test]
	public void StreamStartTimeoutReturnsStructuredFailureWithoutRegisteringStream()
	{
		var sessionService = new FakeAppSessionService();
		sessionService.Session.StartStreamHandler = (_, _) => throw new DeepFlowTest.Cli.CliException(
			DeepFlowTest.Cli.CliErrorCodes.PipeFailed,
			"The operation has timed out.");
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var failed = StreamTools.StartStream(
			fixture.Runner,
			fixture.Host,
			fixture.Streams,
			fixture.Options,
			kind: ProtocolConstants.StreamKinds.VisualTree,
			intervalMs: 1_000);
		var ping = TargetTools.PingTarget(fixture.Runner, fixture.Host, fixture.Options);

		Assert.That(failed.Success, Is.False);
		Assert.That(failed.Error!.Code, Is.EqualTo(DeepFlowTest.Cli.CliErrorCodes.PipeFailed));
		Assert.That(failed.Error.Message, Does.Contain("timed out"));
		Assert.That(failed.Recovery, Does.Contain("Retry ping"));
		Assert.That(fixture.Streams.ListActiveStreams(), Is.Empty);
		Assert.That(ping.Success, Is.True);
	}

	[TestCase(ProtocolConstants.ErrorCodes.InvalidArguments, DeepFlowTest.Cli.CliErrorCodes.InvalidArguments)]
	[TestCase(ProtocolConstants.ErrorCodes.UnsupportedTarget, DeepFlowTest.Cli.CliErrorCodes.UnsupportedTarget)]
	[TestCase(ProtocolConstants.ErrorCodes.UnsupportedCommand, DeepFlowTest.Cli.CliErrorCodes.UnsupportedTarget)]
	[TestCase(ProtocolConstants.ErrorCodes.TargetExited, DeepFlowTest.Cli.CliErrorCodes.TargetExited)]
	public void ProtocolErrorCodesMapToCliErrorClasses(string protocolError, string expectedCliError)
	{
		Assert.That(DeepFlowTest.Cli.ProtocolErrorMapper.Map(protocolError), Is.EqualTo(expectedCliError));
	}

	[Test]
	public void ActionToolsDenyMutationsByDefault()
	{
		var fixture = McpTestHost.CreateHost();
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = ActionTools.ClickElement(
			fixture.Runner,
			fixture.Host,
			fixture.Cache,
			fixture.Options,
			targetId: "0002");

		Assert.That(response.Success, Is.False);
		Assert.That(response.Error!.Code, Is.EqualTo("action-denied"));
	}

	[Test]
	public void ClickSendsPayloadCommandWhenActionsAreAllowed()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(options: McpTestHost.Options(allowActions: true), sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = ActionTools.ClickElement(
			fixture.Runner,
			fixture.Host,
			fixture.Cache,
			fixture.Options,
			targetId: "0002",
			button: "right");

		Assert.That(response.Success, Is.True);
		var command = sessionService.Session.Commands.OfType<ClickCommandRequest>().Single();
		Assert.That(command.TargetId, Is.EqualTo("button-0002"));
		Assert.That(command.MouseButton, Is.EqualTo(MouseButtonKind.Right));
	}

	[Test]
	public void ScreenshotFileWritesRequirePolicy()
	{
		var fixture = McpTestHost.CreateHost();
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = ScreenshotTools.CaptureScreenshot(
			fixture.Runner,
			fixture.Host,
			fixture.Cache,
			fixture.Resources,
			fixture.Options,
			outputPath: "capture.png");

		Assert.That(response.Success, Is.False);
		Assert.That(response.Error!.Code, Is.EqualTo("action-denied"));
	}

	[Test]
	public void ScreenshotCanReturnInlineBytesAndResourceReference()
	{
		var fixture = McpTestHost.CreateHost();
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = ScreenshotTools.CaptureScreenshot(
			fixture.Runner,
			fixture.Host,
			fixture.Cache,
			fixture.Resources,
			fixture.Options,
			includeBase64: true);
		var data = (ScreenshotCaptureData)response.Data!;

		Assert.That(response.Success, Is.True);
		Assert.That(data.Screenshot.BytesBase64, Is.EqualTo("AQIDBA=="));
		Assert.That(data.Resource.Uri, Is.EqualTo(DeepFlowResourceNames.LatestScreenshot));
		Assert.That(DeepFlowResources.LatestScreenshot(fixture.ServiceProvider), Does.Contain("AQIDBA=="));
	}

	[Test]
	public void ScreenshotWithFullTargetIdDoesNotForceVisualTreeRefresh()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = ScreenshotTools.CaptureScreenshot(
			fixture.Runner,
			fixture.Host,
			fixture.Cache,
			fixture.Resources,
			fixture.Options,
			targetId: "button-0002");

		Assert.That(response.Success, Is.True);
		Assert.That(sessionService.Session.Commands.OfType<GetVisualTreeCommandRequest>(), Is.Empty);
		Assert.That(sessionService.Session.Commands.OfType<ScreenshotCommandRequest>().Single().TargetId, Is.EqualTo("button-0002"));
	}

	[Test]
	public void StreamStopSendsPayloadStopCommand()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var started = StreamTools.StartStream(fixture.Runner, fixture.Host, fixture.Streams, fixture.Options);
		var streamId = ((DeepFlowTest.Mcp.Hosting.StreamStartResult)started.Data!).StreamId;
		var stopped = StreamTools.StopStream(fixture.Runner, fixture.Streams, streamId);

		Assert.That(started.Success, Is.True);
		Assert.That(stopped.Success, Is.True);
		Assert.That(sessionService.Session.Commands.OfType<StopSendingCommandRequest>().Single().SubscriptionId, Is.EqualTo("sub-1"));
	}

	[Test]
	public void StreamStartAcceptsSemanticRecordingKind()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var started = StreamTools.StartStream(
			fixture.Runner,
			fixture.Host,
			fixture.Streams,
			fixture.Options,
			kind: ProtocolConstants.StreamKinds.SemanticRecording,
			intervalMs: 100);

		Assert.That(started.Success, Is.True);
		var request = sessionService.Session.Commands.OfType<StartSendingCommandRequest>().Single();
		Assert.That(request.StreamKind, Is.EqualTo(ProtocolConstants.StreamKinds.SemanticRecording));
		Assert.That(request.SemanticRecording, Is.Not.Null);
		Assert.That(request.SemanticRecording!.MaxNodeCount, Is.EqualTo(fixture.Options.Value.TreeLimit));
		Assert.That(request.PropNames, Does.Contain(KnownProperties.Header));
	}

	[Test]
	public void SemanticRecordingStreamReadReturnsCondensedPrunedText()
	{
		var sessionService = new FakeAppSessionService();
		sessionService.Session.StreamFrames =
		[
			new StreamMessage(
				"sub-1",
				ProtocolConstants.StreamKinds.SemanticRecording,
				1,
				new SemanticRecordingBatch
				{
					RecordingId = "recording",
					Frames =
					[
						new SemanticRecordingFrame
						{
							RecordingId = "recording",
							FrameKind = "snapshot",
							SequenceNumber = 1,
							Snapshot = VisualTreeSnapshot.Create(1, new[]
							{
								Node("window-0001", type: "Window", automationId: "MainWindow", childIds: ["grid-0002"], isRoot: true),
								Node("grid-0002", type: "Grid", name: "LayoutGrid", parentId: "window-0001", childIds: ["button-0003"]),
								Node("button-0003", type: "Button", automationId: "SubmitButton", text: "Submit", parentId: "grid-0002"),
							}),
						},
					],
				}),
		];
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var started = StreamTools.StartStream(
			fixture.Runner,
			fixture.Host,
			fixture.Streams,
			fixture.Options,
			kind: ProtocolConstants.StreamKinds.SemanticRecording);
		var streamId = ((StreamStartResult)started.Data!).StreamId;
		Assert.That(SpinWait.SpinUntil(() => sessionService.Session.LastStreamSession!.ReadCount >= 1, 1_000), Is.True);
		var read = StreamTools.ReadStream(fixture.Runner, fixture.Streams, streamId, maxFrames: 10);
		var data = (StreamReadResult)read.Data!;

		Assert.That(read.Success, Is.True);
		Assert.That(data.Frames, Is.Empty);
		Assert.That(data.FrameCount, Is.EqualTo(1));
		Assert.That(data.Recording, Is.Not.Null);
		Assert.That(data.Recording!.Text, Does.Contain("dft-condensed/1"));
		Assert.That(data.Recording.Text, Does.Contain("Button [0003] #SubmitButton"));
		Assert.That(data.Recording.Text, Does.Not.Contain("Grid [0002]"));
	}

	[Test]
	public void StreamReadReportsBufferedFramesAndDrops()
	{
		var options = McpTestHost.Options();
		options.StreamBufferSize = 1;
		var sessionService = new FakeAppSessionService();
		sessionService.Session.StreamFrames =
		[
			new StreamMessage("sub-1", ProtocolConstants.StreamKinds.VisualTree, 1, new { value = 1 }),
			new StreamMessage("sub-1", ProtocolConstants.StreamKinds.VisualTree, 2, new { value = 2 }),
			new StreamMessage("sub-1", ProtocolConstants.StreamKinds.VisualTree, 3, new { value = 3 }),
		];
		var fixture = McpTestHost.CreateHost(options: options, sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var started = StreamTools.StartStream(fixture.Runner, fixture.Host, fixture.Streams, fixture.Options);
		var streamId = ((StreamStartResult)started.Data!).StreamId;
		Assert.That(SpinWait.SpinUntil(() => sessionService.Session.LastStreamSession!.ReadCount >= 3, 1_000), Is.True);
		var read = StreamTools.ReadStream(fixture.Runner, fixture.Streams, streamId, maxFrames: 10);
		var data = (StreamReadResult)read.Data!;

		Assert.That(read.Success, Is.True);
		Assert.That(data.Frames.Select(static frame => frame.Sequence), Is.EqualTo(new long[] { 3 }));
		Assert.That(data.DroppedFrames, Is.EqualTo(2));
	}

	[Test]
	public void DetachStopsActiveStreams()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		StreamTools.StartStream(fixture.Runner, fixture.Host, fixture.Streams, fixture.Options);
		fixture.Host.Detach();

		Assert.That(sessionService.Session.LastStreamSession!.Disposed, Is.True);
		Assert.That(sessionService.Session.Commands.OfType<StopSendingCommandRequest>().Single().SubscriptionId, Is.EqualTo("sub-1"));
	}

	[Test]
	public void DeadTargetStatusStopsActiveStreams()
	{
		var resolver = new FakeTargetResolver();
		var targetProcess = (FakeTargetProcess)resolver.Target.TargetProcess!;
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(resolver: resolver, sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });
		StreamTools.StartStream(fixture.Runner, fixture.Host, fixture.Streams, fixture.Options);

		targetProcess.HasExited = true;
		var status = fixture.Host.Status;

		Assert.That(status.IsAlive, Is.False);
		Assert.That(sessionService.Session.LastStreamSession!.Disposed, Is.True);
		Assert.That(sessionService.Session.Commands.OfType<StopSendingCommandRequest>().Single().SubscriptionId, Is.EqualTo("sub-1"));
	}

	[Test]
	public void VisualTreeCacheHitsAndRefreshMisses()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options);
		InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options);
		InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options, refresh: true);

		Assert.That(sessionService.Session.Commands.OfType<GetVisualTreeCommandRequest>().Count(), Is.EqualTo(2));
	}

	[Test]
	public void VisualTreeCacheRetainsIndependentPropertySets()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options, properties: "Name");
		InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options, properties: "Text");
		InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options, properties: "Name");
		InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options, properties: "Text");

		Assert.That(sessionService.Session.Commands.OfType<GetVisualTreeCommandRequest>().Count(), Is.EqualTo(2));
	}

	[Test]
	public void ActionInvalidatesVisualTreeCache()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(options: McpTestHost.Options(allowActions: true), sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options);
		ActionTools.FocusElement(fixture.Runner, fixture.Host, fixture.Cache, fixture.Options, targetId: "button-0002", after: "none");
		InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options);

		Assert.That(sessionService.Session.Commands.OfType<GetVisualTreeCommandRequest>().Count(), Is.EqualTo(2));
	}

	[Test]
	public void ActionDefaultReturnsCondensedDeltaAfterResult()
	{
		var before = VisualTreeSnapshot.Create(1, new[]
		{
			Node("window-0001", type: "Window", automationId: "MainWindow", childIds: ["grid-0002"], isRoot: true),
			Node("grid-0002", type: "Grid", name: "LayoutGrid", parentId: "window-0001", childIds: ["button-0003"]),
			Node("button-0003", type: "Button", automationId: "SubmitButton", text: "Before", parentId: "grid-0002"),
		});
		var after = VisualTreeSnapshot.Create(2, new[]
		{
			Node("window-0001", type: "Window", automationId: "MainWindow", childIds: ["grid-0002"], isRoot: true),
			Node("grid-0002", type: "Grid", name: "LayoutGrid", parentId: "window-0001", childIds: ["button-0003"]),
			Node("button-0003", type: "Button", automationId: "SubmitButton", text: "After", parentId: "grid-0002"),
		});
		var sessionService = new FakeAppSessionService();
		var snapshotReads = 0;
		sessionService.Session.SendHandler = command => command switch
		{
			GetVisualTreeCommandRequest => ++snapshotReads == 1 ? before : after,
			ClickCommandRequest => new StandardIpcResponse { Success = true },
			_ => new StandardIpcResponse { Success = true },
		};
		var fixture = McpTestHost.CreateHost(options: McpTestHost.Options(allowActions: true), sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var response = ActionTools.ClickElement(
			fixture.Runner,
			fixture.Host,
			fixture.Cache,
			fixture.Options,
			targetId: "button-0003");
		var data = (McpActionCommandResult)response.Data!;
		var delta = (McpCondensedRecordingOutput)data.After!;

		Assert.That(response.Success, Is.True);
		Assert.That(delta.Format, Is.EqualTo("condensed-agent"));
		Assert.That(delta.Text, Does.Contain("@1 delta"));
		Assert.That(delta.Text, Does.Contain("Button [0003] #SubmitButton"));
		Assert.That(delta.Text, Does.Contain("After"));
		Assert.That(delta.Text, Does.Not.Contain("Grid [0002]"));
	}

	[Test]
	public void ActionToolsGenerateExpectedIpcCommands()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(options: McpTestHost.Options(allowActions: true), sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		ActionTools.FocusElement(fixture.Runner, fixture.Host, fixture.Cache, fixture.Options, targetId: "0002", after: "none");
		ActionTools.DragAndDrop(fixture.Runner, fixture.Host, fixture.Cache, fixture.Options, targetId: "0002", destinationTargetId: "0002", durationMs: 250, after: "none");
		ActionTools.TypeText(fixture.Runner, fixture.Host, fixture.Cache, fixture.Options, "hello", targetId: "0002", clearFirst: true, after: "none");
		ActionTools.PressKeys(fixture.Runner, fixture.Host, fixture.Cache, fixture.Options, "Enter", targetId: "0002", delayMs: 5, ensureForeground: false, after: "none");
		ActionTools.SetProperty(fixture.Runner, fixture.Host, fixture.Cache, fixture.Options, KnownProperties.Value, "\"42\"", targetId: "0002", after: "none");
		ActionTools.RaiseEvent(fixture.Runner, fixture.Host, fixture.Cache, fixture.Options, "Click", targetId: "0002", after: "none");
		ActionTools.InvokeOperation(fixture.Runner, fixture.Host, fixture.Cache, fixture.Options, "Select", targetId: "0002", after: "none");

		Assert.That(sessionService.Session.Commands.OfType<FocusCommandRequest>().Single().TargetId, Is.EqualTo("button-0002"));
		var drag = sessionService.Session.Commands.OfType<DragAndDropCommandRequest>().Single();
		Assert.That(drag.TargetId, Is.EqualTo("button-0002"));
		Assert.That(drag.DestinationTargetId, Is.EqualTo("button-0002"));
		Assert.That(drag.DurationMs, Is.EqualTo(250));
		Assert.That(sessionService.Session.Commands.OfType<TypeTextCommandRequest>().Single().ClearFirst, Is.True);
		Assert.That(sessionService.Session.Commands.OfType<KeyPressCommandRequest>().Single().EnsureForeground, Is.False);
		Assert.That(sessionService.Session.Commands.OfType<SetPropertyCommandRequest>().Single().PropertyValue, Is.EqualTo("42"));
		Assert.That(sessionService.Session.Commands.OfType<KnownRoutedEventCommandRequest>().Single().EventName, Is.EqualTo("Click"));
		Assert.That(sessionService.Session.Commands.OfType<KnownOperationCommandRequest>().Single().Operation, Is.EqualTo("Select"));
	}

	[Test]
	public void DragToolResolvesDestinationSelectorAndForwardsOptions()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(options: McpTestHost.Options(allowActions: true), sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		ActionTools.DragAndDrop(
			fixture.Runner,
			fixture.Host,
			fixture.Cache,
			fixture.Options,
			targetId: "0002",
			destinationAutomationId: "SubmitButton",
			durationMs: 640,
			holdMs: 20,
			stepIntervalMs: 8,
			postDropWaitMs: 30,
			sourceAnchorX: 0.15,
			sourceAnchorY: 0.25,
			destinationAnchorX: 0.75,
			destinationAnchorY: 0.85,
			ensureForeground: false,
			validateSameProcess: false,
			after: "none");

		Assert.That(sessionService.Session.Commands.OfType<GetVisualTreeCommandRequest>().Count(), Is.EqualTo(1));
		var drag = sessionService.Session.Commands.OfType<DragAndDropCommandRequest>().Single();
		Assert.That(drag.TargetId, Is.EqualTo("button-0002"));
		Assert.That(drag.DestinationTargetId, Is.EqualTo("button-0002"));
		Assert.That(drag.DurationMs, Is.EqualTo(640));
		Assert.That(drag.HoldMs, Is.EqualTo(20));
		Assert.That(drag.StepIntervalMs, Is.EqualTo(8));
		Assert.That(drag.PostDropWaitMs, Is.EqualTo(30));
		Assert.That(drag.SourceAnchorX, Is.EqualTo(0.15));
		Assert.That(drag.SourceAnchorY, Is.EqualTo(0.25));
		Assert.That(drag.DestinationAnchorX, Is.EqualTo(0.75));
		Assert.That(drag.DestinationAnchorY, Is.EqualTo(0.85));
		Assert.That(drag.UseInjectedEvents, Is.True);
		Assert.That(drag.EnsureForeground, Is.False);
		Assert.That(drag.ValidateSameProcess, Is.False);
	}

	[Test]
	public void LatestResourcesAreUpdatedByTools()
	{
		var fixture = McpTestHost.CreateHost();
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		InspectTools.GetVisualTree(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options);
		InspectTools.GetNode(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options, "0002");
		InspectTools.GetBindingFailures(fixture.Runner, fixture.Host, fixture.Resources, fixture.Options);
		ScreenshotTools.CaptureScreenshot(fixture.Runner, fixture.Host, fixture.Cache, fixture.Resources, fixture.Options);

		Assert.That(DeepFlowResources.LatestVisualTree(fixture.ServiceProvider), Does.Contain("SubmitButton"));
		Assert.That(DeepFlowResources.LatestNode(fixture.ServiceProvider), Does.Contain("button-0002"));
		Assert.That(DeepFlowResources.LatestBindingFailures(fixture.ServiceProvider), Does.Contain("failures"));
		Assert.That(DeepFlowResources.LatestScreenshot(fixture.ServiceProvider), Does.Contain("AQIDBA=="));
	}

	[Test]
	public void LiveResourceReadsFreshVisualTree()
	{
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		fixture.Host.Attach(new DeepFlowTest.Mcp.Contracts.McpTargetSelector { ProcessId = 1234 });

		var json = DeepFlowResources.LiveVisualTree(fixture.ServiceProvider);

		Assert.That(json, Does.Contain("SubmitButton"));
		Assert.That(sessionService.Session.Commands.OfType<GetVisualTreeCommandRequest>().Single().AsSnapshot, Is.True);
	}

	[Test]
	public void PromptsReferenceExistingTools()
	{
		var fixture = McpTestHost.CreateHost();
		var promptText = string.Join("\n", DeepFlowPrompts.InspectUi(), DeepFlowPrompts.DriveUi(), DeepFlowPrompts.DiagnoseUiFailure(), DeepFlowPrompts.AuthorTest());
		var knownTools = fixture.Resources.ListKnownToolNames();
		var referencedTools = knownTools.Where(promptText.Contains).ToArray();

		Assert.That(referencedTools, Is.SupersetOf(new[]
		{
			"deepflow_target_status",
			"deepflow_get_visual_tree",
			"deepflow_find_elements",
			"deepflow_click_element",
			"deepflow_get_binding_failures",
		}));
	}

	[Test]
	public void UnexpectedToolErrorsAreSanitizedAndLogged()
	{
		var fixture = McpTestHost.CreateHost();

		var response = fixture.Runner.Run(() => throw new System.InvalidOperationException("sensitive detail"));

		Assert.That(response.Success, Is.False);
		Assert.That(response.Error!.Code, Is.EqualTo(DeepFlowTest.Cli.CliErrorCodes.UnexpectedError));
		Assert.That(response.Error.Message, Does.Not.Contain("sensitive detail"));
		Assert.That(DeepFlowResources.RecentLogs(fixture.ServiceProvider), Does.Contain("Unexpected MCP tool failure"));
	}

	[Test]
	public void ActivityDetailsViewModelShowsToolParametersAndResult()
	{
		var viewModel = new ActivityEventViewModel(new McpActivityEvent
		{
			Kind = "tool.success",
			Name = "deepflow_click_element",
			Status = "success",
			Details = new ToolActivityDetails
			{
				Parameters = new { targetId = "button-0002", button = "right" },
				Result = new { action = "click", ok = true },
			},
		});

		Assert.That(viewModel.DetailsText, Does.Contain("Tool: deepflow_click_element"));
		Assert.That(viewModel.DetailsText, Does.Contain("Parameters:"));
		Assert.That(viewModel.DetailsText, Does.Contain("button-0002"));
		Assert.That(viewModel.DetailsText, Does.Contain("Result:"));
		Assert.That(viewModel.DetailsText, Does.Contain("\"action\": \"click\""));
	}

	private static VisualTreeNodeDto Node(
		string id,
		string? parentId = null,
		bool isRoot = false,
		IReadOnlyList<string>? childIds = null,
		string type = "Window",
		string? automationId = null,
		string? name = null,
		string? text = null) =>
		new()
		{
			TargetId = id,
			ParentId = parentId,
			ChildIds = [.. (childIds ?? [])],
			IsRoot = isRoot,
			TypeName = type,
			FrameworkTypeName = "System.Windows.Controls." + type,
			Properties = new Dictionary<string, object?>
			{
				[KnownProperties.Name] = name ?? id,
				[KnownProperties.AutomationId] = automationId ?? string.Empty,
				[KnownProperties.Text] = text ?? string.Empty,
				[KnownProperties.Content] = text ?? string.Empty,
				[KnownProperties.IsVisible] = true,
				[KnownProperties.IsEnabled] = true,
			},
		};

	private static ProcessSnapshot Process(int pid, string name, bool isCandidate, bool hasExited = false, bool hasWindow = true, string? frameworkFamily = null) =>
		new()
		{
			ProcessId = pid,
			ProcessName = name,
			MainWindowTitle = hasWindow ? name : string.Empty,
			TopLevelWindows = hasWindow ? [new ProcessWindowSnapshot { Hwnd = pid, Title = name }] : [],
			IsLikelyWpfCandidate = isCandidate,
			FrameworkFamily = frameworkFamily ?? (isCandidate ? "wpf" : string.Empty),
			HasExited = hasExited,
		};
}
