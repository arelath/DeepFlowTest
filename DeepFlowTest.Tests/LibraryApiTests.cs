namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using FakeSession = DeepFlowTest.Tests.Fakes.FakeAppDriverCommandSession;

[TestFixture]
public sealed class LibraryApiTests
{
	[Test]
	public void VirtualPointerOptionsConfigurePayloadAfterSessionCreation()
	{
		var session = new FakeSession(StandardIpcResponse.Ok());
		var options = new AppDriverOptions
		{
			AutoSemanticRecordingEnabled = false,
			VirtualPointer = new VirtualPointerOptions
			{
				Enabled = true,
				HideDelay = TimeSpan.FromMilliseconds(250),
			},
		};

		using var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			session,
			options);

		var configure = session.SentCommands.OfType<ConfigureDiagnosticsCommandRequest>().Single();
		Assert.That(configure.VirtualPointer, Is.Not.Null);
		Assert.That(configure.VirtualPointer!.Enabled, Is.True);
		Assert.That(configure.VirtualPointer.HideDelayMs, Is.EqualTo(250));
	}

	[Test]
	public void PublicApiCanFindActTypeAndScreenshotThroughSession()
	{
		var session = new FakeSession(
			FindMatch("button", "runButton", "Button"),
			StandardIpcResponse.Ok(),
			FindMatch("input", "nameBox", "TextBox"),
			StandardIpcResponse.Ok(),
			new ScreenshotCommandResponse
			{
				TargetId = "input",
				Format = ImageFormat.Png,
				Width = 12,
				Height = 8,
				ByteCount = 3,
				BytesBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
			},
			new ScreenshotCommandResponse
			{
				TargetId = "input",
				Format = ImageFormat.Png,
				Width = 12,
				Height = 8,
				ByteCount = 3,
				BytesBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
			});
		var backend = new FakeBackend(session);
		var factory = new AppDriverFactory(backend, (_, _) => session);

		using var driver = factory.Launch("HelloWorld.exe");
		var button = driver.GetElement(ElementSelector.ByName("runButton"));
		button.Click();
		var textBox = driver.GetElement(ElementSelector.ByName("nameBox"));
		textBox.Type("Codex", clearFirst: true);
		var screenshot = textBox.Screenshot(ImageFormat.Png);

		Assert.That(backend.Connection!.OwnsProcess, Is.True);
		Assert.That(session.SentCommands.OfType<FindElementCommandRequest>().Count(), Is.EqualTo(2));
		Assert.That(session.SentCommands.OfType<ClickCommandRequest>().Single().TargetId, Is.EqualTo("button"));
		Assert.That(session.SentCommands.OfType<TypeTextCommandRequest>().Single().Text, Is.EqualTo("Codex"));
		Assert.That(screenshot.Length, Is.EqualTo(3));
	}

	[Test]
	public void CompatibilityLaunchAndScreenshotOverloadsCompileAndSendExpectedCommands()
	{
		var session = new FakeSession(new ScreenshotCommandResponse
		{
			Format = ImageFormat.Jpeg,
			Width = 1,
			Height = 1,
			ByteCount = 2,
			BytesBase64 = Convert.ToBase64String(new byte[] { 5, 6 }),
		},
		new ScreenshotCommandResponse
		{
			Format = ImageFormat.Jpeg,
			Width = 1,
			Height = 1,
			ByteCount = 2,
			BytesBase64 = Convert.ToBase64String(new byte[] { 5, 6 }),
		});
		var backend = new FakeBackend(session);
		var factory = new AppDriverFactory(backend, (_, _) => session);

		using var driver = factory.Launch("HelloWorld.exe", "--demo");
		var bytes = driver.Screenshot(ImageFormat.Jpeg);

		Assert.That(backend.LastLaunchOptions!.Arguments, Is.EqualTo("--demo"));
		Assert.That(bytes, Is.EqualTo(new byte[] { 5, 6 }));
		Assert.That(session.SentCommands.OfType<ScreenshotCommandRequest>().Select(static command => command.Format), Is.EqualTo(new[] { ImageFormat.Jpeg, ImageFormat.Jpeg }));
	}

	[Test]
	public void RecordApiStartsFfmpegRecordingUntilDisposed()
	{
		var fake = new FakeRecordingProcess();
		var starts = new List<ProcessStartInfo>();
		var previousFactory = AppDriver.RecordingProcessFactory;
		var previousFfmpegPath = AppDriver.RecordingFfmpegPathOverride;
		AppDriver.RecordingFfmpegPathOverride = Path.Combine(Path.GetTempPath(), "fake-ffmpeg.exe");
		AppDriver.RecordingProcessFactory = startInfo =>
		{
			starts.Add(startInfo);
			return fake;
		};

		try
		{
			using (AppDriver.Record(Path.Combine(Path.GetTempPath(), "deepflow-recording.mp4"), "Main Window"))
			{
				Assert.That(starts.Single().FileName, Is.EqualTo(AppDriver.RecordingFfmpegPathOverride));
				Assert.That(starts.Single().Arguments, Does.Contain("title=\"Main Window\""));
			}

			Assert.That(fake.Waited, Is.True);
			Assert.That(fake.Input, Does.Contain("q"));
			Assert.That(fake.RegisteredForParentClose, Is.True);
		}
		finally
		{
			AppDriver.RecordingProcessFactory = previousFactory;
			AppDriver.RecordingFfmpegPathOverride = previousFfmpegPath;
		}
	}

	[Test]
	public void SemanticRecordingApiStartsStreamWritesJsonArrayAndStops()
	{
		var session = new FakeSemanticRecordingCommandSession();
		using var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			session);
		var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "library-semantic-recording.json");
		if (File.Exists(path))
			File.Delete(path);

		using (var recording = driver.StartSemanticRecording(path, new SemanticRecordingOptions
		{
			Interval = TimeSpan.FromMilliseconds(60),
			TextIdleDuration = TimeSpan.FromMilliseconds(123),
			MaxBatchFrames = 7,
			OutputFormat = SemanticRecordingOutputFormat.CompactJson,
		}))
		{
			Assert.That(SpinWait.SpinUntil(() => recording.FramesWritten > 0, TimeSpan.FromSeconds(2)), Is.True);
		}

		var start = session.StartRequest!;
		Assert.That(start.StreamKind, Is.EqualTo(ProtocolConstants.StreamKinds.SemanticRecording));
		Assert.That(start.SemanticRecording!.TextIdleMs, Is.EqualTo(123));
		Assert.That(start.SemanticRecording.MaxBatchFrames, Is.EqualTo(7));
		Assert.That(start.SemanticRecording.MaxNodeCount, Is.EqualTo(VisualTreeDefaults.DefaultMaxNodeCount));
		Assert.That(session.SentCommands.OfType<StopSendingCommandRequest>().Single().SubscriptionId, Is.EqualTo("sub-1"));
		var recordingText = File.ReadAllText(path);
		var frames = JArray.Parse(recordingText);
		Assert.That(frames, Has.Count.GreaterThan(0));
		Assert.That(frames.Any(static frame => (string?)frame["kind"] == "action"), Is.True);
		Assert.That(recordingText, Does.Not.Contain("\"frameKind\""));
	}

	[Test]
	public void SemanticRecordingBatchCallbackReceivesRawFramesAndDoesNotStopFileWritingWhenItThrows()
	{
		var session = new FakeSemanticRecordingCommandSession();
		using var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			session);
		var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "library-semantic-recording-callback.json");
		if (File.Exists(path))
			File.Delete(path);

		var receivedFrameCount = 0;
		Exception? callbackError = null;
		using (var recording = driver.StartSemanticRecording(path, new SemanticRecordingOptions
		{
			OutputFormat = SemanticRecordingOutputFormat.CompactJson,
			BatchReceived = batch =>
			{
				receivedFrameCount += batch.Frames.Count;
				throw new InvalidOperationException("viewer failed");
			},
			BatchReceivedError = ex => callbackError = ex,
		}))
		{
			Assert.That(SpinWait.SpinUntil(() => recording.FramesWritten > 0, TimeSpan.FromSeconds(2)), Is.True);
		}

		Assert.That(receivedFrameCount, Is.GreaterThan(0));
		Assert.That(callbackError, Is.TypeOf<InvalidOperationException>());
		var frames = JArray.Parse(File.ReadAllText(path));
		Assert.That(frames, Has.Count.GreaterThan(0));
	}

	[Test]
	public void SemanticRecordingDisposeWaitsForInitialFrameSoShortTestsStillLeaveALog()
	{
		var session = new FakeSemanticRecordingCommandSession(firstFrameDelayMs: 100);
		using var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			session);
		var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "library-short-semantic-recording.json");
		if (File.Exists(path))
			File.Delete(path);

		using (driver.StartSemanticRecording(path, new SemanticRecordingOptions
		{
			Interval = TimeSpan.FromMilliseconds(60),
			TextIdleDuration = TimeSpan.FromMilliseconds(123),
			MaxBatchFrames = 7,
			OutputFormat = SemanticRecordingOutputFormat.CompactJson,
		}))
		{
		}

		var recordingText = File.ReadAllText(path);
		var frames = JArray.Parse(recordingText);
		Assert.That(frames, Has.Count.GreaterThan(0));
		Assert.That(frames.Any(static frame => (string?)frame["kind"] == "action"), Is.True);
		Assert.That(recordingText, Does.Not.Contain("\"frameKind\""));
	}

	[Test]
	public void FailureOnlyDiagnosticsStartsBufferedRecordingByDefaultWithoutKeepingSuccessArtifacts()
	{
		var session = new FakeSemanticRecordingCommandSession();
		var outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "library-failure-only-success");
		if (Directory.Exists(outputDirectory))
			Directory.Delete(outputDirectory, recursive: true);
		var options = new AppDriverOptions
		{
			AutomaticDiagnostics = new AutomaticDiagnosticsOptions { OutputDirectory = outputDirectory },
		};

		using (var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			session,
			options))
		{
			Assert.That(session.StartRequest, Is.Not.Null);
			Assert.That(options.AutoSemanticRecordingOutputPath, Is.Null);
			Assert.That(driver.AutomaticSemanticRecordingOutputPath, Is.Null);
			Assert.That(SpinWait.SpinUntil(() => session.SentCommands.OfType<StartSendingCommandRequest>().Any(), TimeSpan.FromSeconds(2)), Is.True);
		}

		Assert.That(session.SentCommands.OfType<StopSendingCommandRequest>().Single().SubscriptionId, Is.EqualTo("sub-1"));
		Assert.That(Directory.Exists(outputDirectory), Is.False);
	}

	[Test]
	public void AutoSemanticRecordingCanBeDisabledExplicitly()
	{
		var session = new FakeSemanticRecordingCommandSession();
		var options = new AppDriverOptions
		{
			AutomaticDiagnostics = new AutomaticDiagnosticsOptions { Mode = AutomaticDiagnosticsMode.Off },
		};

		using (AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			session,
			options))
		{
			Assert.That(session.StartRequest, Is.Null);
			Assert.That(options.AutoSemanticRecordingOutputPath, Is.Null);
		}
	}

	[Test]
	public void AutoSemanticRecordingDoesNotStartImplicitlyWhenConnectionHasNoPayloadPipe()
	{
		var session = new FakeSemanticRecordingCommandSession();
		var options = new AppDriverOptions();
		using var connection = new AppConnection(new AppConnectionOptions
		{
			TargetProcess = new FakeTargetProcess(),
			PipeName = "pipe",
			InjectorState = AppConnectionInjectorState.InjectionSkipped,
			ReusesPipe = false,
		});

		using (AppDriver.CreateForTests(connection, session, options))
		{
			Assert.That(session.StartRequest, Is.Null);
			Assert.That(options.AutoSemanticRecordingOutputPath, Is.Null);
		}
	}

	[Test]
	public void AutoSemanticRecordingOptionStartsStreamAndStopsWithDriver()
	{
		var session = new FakeSemanticRecordingCommandSession();
		var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "library-auto-semantic-recording.dft.txt");
		if (File.Exists(path))
			File.Delete(path);
		var options = new AppDriverOptions
		{
			AutoSemanticRecordingOutputPath = path,
			AutoSemanticRecordingOptions = new SemanticRecordingOptions
			{
				Interval = TimeSpan.FromMilliseconds(75),
				TextIdleDuration = TimeSpan.FromMilliseconds(25),
			},
		};

		using (AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			session,
			options))
		{
			Assert.That(session.StartRequest, Is.Not.Null);
			Assert.That(session.StartRequest!.StreamKind, Is.EqualTo(ProtocolConstants.StreamKinds.SemanticRecording));
			Assert.That(session.StartRequest.IntervalMs, Is.EqualTo(75));
			Assert.That(session.StartRequest.SemanticRecording!.TextIdleMs, Is.EqualTo(25));
			Assert.That(session.StartRequest.SemanticRecording.MaxNodeCount, Is.EqualTo(VisualTreeDefaults.DefaultMaxNodeCount));
			Assert.That(SpinWait.SpinUntil(() => File.Exists(path) && ReadAllTextShared(path).Contains("@1 action", StringComparison.Ordinal), TimeSpan.FromSeconds(2)), Is.True);
		}

		Assert.That(File.ReadAllText(path), Does.StartWith("dft-condensed/1 profile=agent source=compact-json"));
		Assert.That(session.SentCommands.OfType<StopSendingCommandRequest>().Single().SubscriptionId, Is.EqualTo("sub-1"));
	}

	[Test]
	public void AutomaticSemanticRecordingStartFailureDoesNotFailDriverConstruction()
	{
		var process = new FakeTargetProcess();
		using var connection = AppConnection.ForAttach(process, "pipe");
		var options = new AppDriverOptions
		{
			AutoSemanticRecordingOutputPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "library-auto-semantic-recording-failure.json"),
		};

		using (var driver = AppDriver.CreateForTests(connection, new FakeSession(), options))
		{
			Assert.That(driver.Diagnostics.Select(static diagnostic => diagnostic.Code), Does.Contain("automatic-recording-start-failed"));
			Assert.That(connection.IsDisposed, Is.False);
		}

		Assert.That(connection.IsDisposed, Is.True);
		Assert.That(process.DisposeCount, Is.EqualTo(1));
	}

	[Test]
	public void CompatibilitySystemDialogHelpersFindSetAndInvokeDialogOperations()
	{
		var response = new FindElementCommandResponse
		{
			Status = ProtocolConstants.Statuses.Ok,
			Matches =
			{
				new FindElementMatchResponse
				{
					TargetId = "dialog",
					TypeName = "Dialog",
					Properties = { ["Name"] = "Open" },
				},
			},
			MatchCount = 1,
		};
		var session = new FakeSession(
			response,
			StandardIpcResponse.Ok(),
			StandardIpcResponse.Ok());
		using var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			session);

		var returned = driver.HandleFileDialog(@"C:\temp\file.txt");

		Assert.That(returned, Is.SameAs(driver));
		var set = session.SentCommands.OfType<SetPropertyCommandRequest>().Single();
		var operation = session.SentCommands.OfType<KnownOperationCommandRequest>().Single();
		Assert.That(set.TargetId, Is.EqualTo("dialog"));
		Assert.That(set.PropertyName, Is.EqualTo("FileName"));
		Assert.That(set.PropertyValue, Is.EqualTo(@"C:\temp\file.txt"));
		Assert.That(operation.Operation, Is.EqualTo("AcceptDialog"));
	}

	[Test]
	public void AppScreenshotWaitsForAdjacentStableCapture()
	{
		var first = Convert.ToBase64String(new byte[] { 1 });
		var stable = Convert.ToBase64String(new byte[] { 2 });
		var session = new FakeSession(
			new ScreenshotCommandResponse { Format = ImageFormat.Png, ByteCount = 1, BytesBase64 = first },
			new ScreenshotCommandResponse { Format = ImageFormat.Png, ByteCount = 1, BytesBase64 = stable },
			new ScreenshotCommandResponse { Format = ImageFormat.Png, ByteCount = 1, BytesBase64 = stable });
		using var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			session);

		var bytes = driver.Screenshot(ImageFormat.Png);

		Assert.That(bytes, Is.EqualTo(new byte[] { 2 }));
		Assert.That(session.SentCommands.OfType<ScreenshotCommandRequest>().Count(), Is.EqualTo(3));
	}

	[Test]
	public void PublicApiAttachLeavesTargetAliveOnDispose()
	{
		var backend = new FakeBackend(new FakeSession());
		var factory = new AppDriverFactory(backend, (_, _) => new FakeSession());

		using var driver = factory.AttachTo(123);
		driver.Dispose();

		Assert.That(backend.Process!.KillCount, Is.EqualTo(0));
		Assert.That(driver.Connection.OwnsProcess, Is.False);
	}

	[Test]
	public void CompatibilityAppDriverProcessExposesRawProcess()
	{
		using var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new TargetProcess(Process.GetCurrentProcess()), "pipe"),
			new FakeSession());

		Assert.That(driver.Process.Id, Is.EqualTo(Process.GetCurrentProcess().Id));
	}

	[Test]
	public void CompatibilityReflectionAndIpcHelpersWork()
	{
		var target = new ReflectionTarget();
		typeof(ReflectionTarget).InvokeOn(target, "SetSecret", "value");
		target.SetField("field", 42);
		target.SetProperty("Name", "updated");
		var dictionary = new ClickCommandRequest { TargetId = "button", MouseButton = MouseButtonKind.Right }.ToDictionary();

		Assert.That(target.Property<string>("Name"), Is.EqualTo("updated"));
		Assert.That(target.Field<int>("field"), Is.EqualTo(42));
		Assert.That(target.Invoke<string>("ReadSecret"), Is.EqualTo("value"));
		Assert.That(dictionary["Kind"], Is.EqualTo(ProtocolConstants.Commands.Click));
		Assert.That(dictionary["TargetId"], Is.EqualTo("button"));
		Assert.That(dictionary["MouseButton"], Is.EqualTo("right"));
	}

	[Test]
	public void CompatibilityElementExpressionLookupSupportsPropertyIndexer()
	{
		var response = new FindElementCommandResponse
		{
			Status = ProtocolConstants.Statuses.Ok,
			Matches =
			{
				new FindElementMatchResponse
				{
					TargetId = "button",
					TypeName = "Button",
					Properties = { ["Name"] = "Run", ["Width"] = 120 },
				},
			},
			MatchCount = 1,
		};
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			new FakeSession(response));

		var element = driver.GetElement(x => x["Name"] == "Run" && x["Width"] > 100);

		Assert.That(element.TargetId, Is.EqualTo("button"));
	}

	[Test]
	public void CompatibilityCustomElementLookupReturnsTypedFluentWrapper()
	{
		var response = new FindElementCommandResponse
		{
			Status = ProtocolConstants.Statuses.Ok,
			Matches =
			{
				new FindElementMatchResponse
				{
					TargetId = "button",
					TypeName = "Button",
					Properties = { ["Name"] = "Run" },
				},
			},
			MatchCount = 1,
		};
		var session = new FakeSession(response, StandardIpcResponse.Ok());
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			session);

		var button = driver.GetElement<RunButton>(x => x["Name"] == "Run");
		var clicked = button.Click();

		Assert.That(button.TargetId, Is.EqualTo("button"));
		Assert.That(clicked, Is.SameAs(button));
		Assert.That(session.SentCommands.OfType<ClickCommandRequest>().Single().TargetId, Is.EqualTo("button"));
	}

	private static FindElementCommandResponse FindMatch(string targetId, string name, string typeName)
	{
		return new FindElementCommandResponse
		{
			Matches =
			{
				new FindElementMatchResponse
				{
					TargetId = targetId,
					TypeName = typeName,
					Properties = { ["Name"] = name },
				},
			},
			MatchCount = 1,
		};
	}

	private static string ReadAllTextShared(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	private sealed class FakeBackend : IAppDriverBackend
	{
		private readonly FakeSession session;

		public FakeBackend(FakeSession session)
		{
			this.session = session;
		}

		public FakeTargetProcess? Process { get; private set; }

		public AppConnection? Connection { get; private set; }

		public AppDriverLaunchOptions? LastLaunchOptions { get; private set; }

		public AppConnection Launch(string executablePath, AppDriverLaunchOptions options)
		{
			Process = new FakeTargetProcess();
			LastLaunchOptions = options;
			Connection = AppConnection.ForLaunch(Process, options.PipeName ?? "launch-pipe");
			return Connection;
		}

		public AppConnection AttachTo(int processId, AppDriverAttachOptions options)
		{
			Process = new FakeTargetProcess { Id = processId };
			Connection = AppConnection.ForAttach(Process, options.PipeName ?? "attach-pipe");
			return Connection;
		}

		public AppConnection AttachTo(string processName, AppDriverAttachOptions options)
		{
			Process = new FakeTargetProcess { ProcessName = processName };
			Connection = AppConnection.ForAttach(Process, options.PipeName ?? "attach-name-pipe");
			return Connection;
		}
	}

	private sealed class RunButton : Element<RunButton>
	{
		public RunButton(Element source)
			: base(source)
		{
		}
	}

	private sealed class ReflectionTarget
	{
		private string secret = string.Empty;

		public int field = -1;

		public string Name { get; private set; } = "initial";

		private void SetSecret(string value)
		{
			secret = value;
		}

		private string ReadSecret() => secret;
	}

	private sealed class FakeRecordingProcess : IRecordingProcess
	{
		private readonly StringWriter input = new();

		public string Input => input.ToString();

		public bool Waited { get; private set; }

		public bool RegisteredForParentClose { get; private set; }

		public TextWriter StandardInput => input;

		public void RegisterForParentClose()
		{
			RegisteredForParentClose = true;
		}

		public void WaitForExit()
		{
			Waited = true;
		}

		public void Dispose()
		{
			input.Dispose();
		}
	}

	private sealed class FakeSemanticRecordingCommandSession : IUnsafeAppDriverCommandSession, IAppDriverStreamingSession
	{
		private readonly int firstFrameDelayMs;

		public FakeSemanticRecordingCommandSession(int firstFrameDelayMs = 0)
		{
			this.firstFrameDelayMs = firstFrameDelayMs;
		}

		public List<IpcCommand> SentCommands { get; } = [];

		public StartSendingCommandRequest? StartRequest { get; private set; }

		public TResponse Send<TResponse>(IpcCommand command)
		{
			SentCommands.Add(command);
			if (command is StopSendingCommandRequest stop)
				return (TResponse)(object)new StopSendingCommandResponse(stop.SubscriptionId, ProtocolConstants.Statuses.Stopped);

			throw new InvalidOperationException("Unexpected command " + command.Kind);
		}

		public IAppDriverStreamSession StartStream(StartSendingCommandRequest command, int timeoutMs)
		{
			StartRequest = command;
			SentCommands.Add(command);
			return new FakeSemanticRecordingStreamSession(command, firstFrameDelayMs);
		}
	}

	private sealed class FakeSemanticRecordingStreamSession : IAppDriverStreamSession
	{
		private readonly int firstFrameDelayMs;
		private int readCount;

		public FakeSemanticRecordingStreamSession(StartSendingCommandRequest command, int firstFrameDelayMs)
		{
			this.firstFrameDelayMs = firstFrameDelayMs;
			Start = new StartSendingCommandResponse("sub-1", command.StreamKind, ProtocolConstants.Statuses.Started)
			{
				IntervalMs = command.IntervalMs,
			};
		}

		public StartSendingCommandResponse Start { get; }

		public StreamMessage? ReadFrame(int timeoutMs, CancellationToken cancellationToken = default)
		{
			if (Interlocked.Increment(ref readCount) > 1)
			{
				Thread.Sleep(10);
				return null;
			}

			if (firstFrameDelayMs > 0 && cancellationToken.WaitHandle.WaitOne(firstFrameDelayMs))
				throw new OperationCanceledException(cancellationToken);

			return new StreamMessage(Start.SubscriptionId, Start.StreamKind, 1, new SemanticRecordingBatch
			{
				RecordingId = "recording",
				BatchSequenceNumber = 1,
				Frames =
				[
					new SemanticRecordingFrame
					{
						RecordingId = "recording",
						FrameKind = "action",
						SequenceNumber = 1,
						Action = new RecordedInputAction
						{
							ActionKind = "type",
							Text = "hello",
							Target = new RecordedTarget
							{
								TargetId = "text",
								TypeName = "TextBox",
								Summary = "TextBox[Name='User']",
							},
						},
					},
				],
			});
		}

		public void Dispose()
		{
		}
	}
}
