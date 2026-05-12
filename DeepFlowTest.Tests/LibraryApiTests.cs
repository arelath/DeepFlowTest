namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class LibraryApiTests
{
	[TearDown]
	public void ResetBackend()
	{
		AppDriver.ResetBackendForTests();
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
				Format = "png",
				Width = 12,
				Height = 8,
				ByteCount = 3,
				BytesBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
			});
		var backend = new FakeBackend(session);
		AppDriver.ConfigureBackendForTests(backend);
		AppDriver.ConfigureSessionFactoryForTests((_, _) => session);

		using var driver = AppDriver.Launch("HelloWorld.exe");
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
	public void WpfPilot2StyleLaunchAndScreenshotOverloadsCompileAndSendExpectedCommands()
	{
		var session = new FakeSession(new ScreenshotCommandResponse
		{
			Format = "jpeg",
			Width = 1,
			Height = 1,
			ByteCount = 2,
			BytesBase64 = Convert.ToBase64String(new byte[] { 5, 6 }),
		});
		var backend = new FakeBackend(session);
		AppDriver.ConfigureBackendForTests(backend);
		AppDriver.ConfigureSessionFactoryForTests((_, _) => session);

		using var driver = AppDriver.Launch("HelloWorld.exe", "--demo");
		var bytes = driver.Screenshot(ImageFormat.Jpeg);

		Assert.That(backend.LastLaunchOptions!.Arguments, Is.EqualTo("--demo"));
		Assert.That(bytes, Is.EqualTo(new byte[] { 5, 6 }));
		Assert.That(session.SentCommands.OfType<ScreenshotCommandRequest>().Single().Format, Is.EqualTo("jpeg"));
	}

	[Test]
	public void PublicApiAttachLeavesTargetAliveOnDispose()
	{
		var backend = new FakeBackend(new FakeSession());
		AppDriver.ConfigureBackendForTests(backend);
		AppDriver.ConfigureSessionFactoryForTests((_, _) => new FakeSession());

		using var driver = AppDriver.AttachTo(123);
		driver.Dispose();

		Assert.That(backend.Process!.KillCount, Is.EqualTo(0));
		Assert.That(driver.Connection.OwnsProcess, Is.False);
	}

	[Test]
	public void WpfPilot2StyleElementExpressionLookupSupportsPropertyIndexer()
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

	private sealed class FakeSession : IAppDriverCommandSession
	{
		private readonly Queue<object> responses;

		public FakeSession(params object[] responses)
		{
			this.responses = new Queue<object>(responses);
		}

		public List<IpcCommand> SentCommands { get; } = new();

		public TResponse Send<TResponse>(IpcCommand command)
		{
			SentCommands.Add(command);
			return (TResponse)responses.Dequeue();
		}
	}

	private sealed class FakeTargetProcess : ITargetProcess
	{
		public int Id { get; set; } = 123;

		public string ProcessName { get; set; } = "target";

		public bool HasExited { get; private set; }

		public int KillCount { get; private set; }

		public void Kill()
		{
			KillCount++;
			HasExited = true;
		}

		public void Dispose()
		{
		}
	}
}
