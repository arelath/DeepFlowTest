namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Tests.Fakes;
using NUnit.Framework;
using FakeSession = DeepFlowTest.Tests.Fakes.FakeAppDriverCommandSession;

[TestFixture]
public sealed class LibraryApiTests
{
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
				Format = "png",
				Width = 12,
				Height = 8,
				ByteCount = 3,
				BytesBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
			},
			new ScreenshotCommandResponse
			{
				TargetId = "input",
				Format = "png",
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
			Format = "jpeg",
			Width = 1,
			Height = 1,
			ByteCount = 2,
			BytesBase64 = Convert.ToBase64String(new byte[] { 5, 6 }),
		},
		new ScreenshotCommandResponse
		{
			Format = "jpeg",
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
		Assert.That(session.SentCommands.OfType<ScreenshotCommandRequest>().Select(static command => command.Format), Is.EqualTo(new[] { "jpeg", "jpeg" }));
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
		}
		finally
		{
			AppDriver.RecordingProcessFactory = previousFactory;
			AppDriver.RecordingFfmpegPathOverride = previousFfmpegPath;
		}
	}

	[Test]
	public void CompatibilitySystemDialogHelpersFindSetAndInvokeDialogOperations()
	{
		var snapshot = VisualTreeSnapshot.Create(1, new[]
		{
			new VisualTreeNodeDto
			{
				TargetId = "dialog",
				TypeName = "Dialog",
				IsRoot = true,
				Properties = { ["Name"] = "Open" },
			},
		});
		var session = new FakeSession(
			snapshot,
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
			new ScreenshotCommandResponse { Format = "png", ByteCount = 1, BytesBase64 = first },
			new ScreenshotCommandResponse { Format = "png", ByteCount = 1, BytesBase64 = stable },
			new ScreenshotCommandResponse { Format = "png", ByteCount = 1, BytesBase64 = stable });
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
	public void CompatibilityElementExpressionLookupSupportsPropertyIndexer()
	{
		var snapshot = VisualTreeSnapshot.Create(1, new[]
		{
			new VisualTreeNodeDto { TargetId = "root", TypeName = "Window", IsRoot = true, Properties = { ["Name"] = "Main" } },
			new VisualTreeNodeDto { TargetId = "button", TypeName = "Button", Properties = { ["Name"] = "Run", ["Width"] = 120 } },
		});
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "pipe"),
			new FakeSession(snapshot));

		var element = driver.GetElement(x => x["Name"] == "Run" && x["Width"] > 100);

		Assert.That(element.TargetId, Is.EqualTo("button"));
	}

	[Test]
	public void CompatibilityCustomElementLookupReturnsTypedFluentWrapper()
	{
		var snapshot = VisualTreeSnapshot.Create(1, new[]
		{
			new VisualTreeNodeDto { TargetId = "button", TypeName = "Button", IsRoot = true, Properties = { ["Name"] = "Run" } },
		});
		var session = new FakeSession(snapshot, StandardIpcResponse.Ok());
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

	private sealed class FakeRecordingProcess : IRecordingProcess
	{
		private readonly StringWriter input = new();

		public string Input => input.ToString();

		public bool Waited { get; private set; }

		public TextWriter StandardInput => input;

		public void WaitForExit()
		{
			Waited = true;
		}

		public void Dispose()
		{
			input.Dispose();
		}
	}
}
