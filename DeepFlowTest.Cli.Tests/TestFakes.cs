namespace DeepFlowTest.Cli.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal static class CliTestHost
{
	public static (int ExitCode, string Stdout, string Stderr) Run(string[] args, CliServices? services = null)
	{
		using var stdout = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
		using var stderr = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
		var exitCode = Program.Run(args, services ?? CreateServices(), stdout, stderr);
		return (exitCode, stdout.ToString(), stderr.ToString());
	}

	public static CliServices CreateServices(
		CliDefaultsStore? defaultsStore = null,
		IProcessSnapshotSource? snapshotSource = null,
		ITargetResolver? targetResolver = null,
		ICliAppSessionService? appSessionService = null)
	{
		return new CliServices(
			defaultsStore ?? new CliDefaultsStore(CreateTempConfigPath()),
			snapshotSource ?? new FakeProcessSnapshotSource(),
			targetResolver,
			appSessionService ?? new FakeAppSessionService());
	}

	public static string CreateTempConfigPath()
	{
		return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DeepFlowTest.Cli.Tests", Guid.NewGuid().ToString("N"), "defaults.json");
	}
}

internal sealed class FakeProcessSnapshotSource : IProcessSnapshotSource
{
	public ProcessSnapshotResult Result { get; set; } = new()
	{
		Processes = Array.Empty<ProcessSnapshot>(),
		Warnings = Array.Empty<ProcessInspectionWarning>(),
	};

	public int CallCount { get; private set; }

	public ProcessSnapshotResult GetSnapshots()
	{
		CallCount++;
		return Result;
	}
}

internal sealed class FakeTargetResolver : ITargetResolver
{
	public TargetSelector? LastSelector { get; private set; }

	public TargetInfo Target { get; set; } = new()
	{
		ProcessId = 1234,
		ProcessName = "TestTarget",
		TargetProcess = new FakeTargetProcess(),
	};

	public TargetInfo Resolve(TargetSelector selector)
	{
		LastSelector = selector;
		return Target;
	}
}

internal sealed class FakeAppSessionService : ICliAppSessionService
{
	public CliAttachOptions? LastOptions { get; private set; }

	public TargetInfo? LastTarget { get; private set; }

	public FakeCliAppSession Session { get; } = new();

	public ICliAppSession Open(TargetInfo target, CliAttachOptions options)
	{
		LastTarget = target;
		LastOptions = options;
		return Session;
	}
}

internal sealed class FakeCliAppSession : ICliAppSession
{
	public HelloCommandResponse Hello { get; set; } = new()
	{
		ProtocolVersion = ProtocolConstants.ProtocolVersion,
		PipeName = "fake-pipe",
		IsReusable = true,
		ProcessId = 1234,
	};

	public List<IpcCommand> Commands { get; } = new();

	public Exception? SendException { get; set; }

	public VisualTreeSnapshot Snapshot { get; set; } = VisualTreeSnapshot.Create(1, new[]
	{
		new VisualTreeNodeDto
		{
			TargetId = "root-0001",
			IsRoot = true,
			TypeName = "Window",
			FrameworkTypeName = "System.Windows.Window",
			ChildIds = new List<string> { "button-0002" },
			Properties = new Dictionary<string, object?>
			{
				["Name"] = "Main",
				["AutomationProperties.Name"] = "Main Window",
				["IsVisible"] = true,
				["IsEnabled"] = true,
			},
		},
		new VisualTreeNodeDto
		{
			TargetId = "button-0002",
			ParentId = "root-0001",
			TypeName = "Button",
			FrameworkTypeName = "System.Windows.Controls.Button",
			Properties = new Dictionary<string, object?>
			{
				["Name"] = "SubmitButton",
				["AutomationProperties.Name"] = "Submit",
				["AutomationProperties.AutomationId"] = "SubmitButton",
				["Text"] = "Submit",
				["IsVisible"] = true,
				["IsEnabled"] = true,
			},
		},
	});

	public ScreenshotCommandResponse Screenshot { get; set; } = new()
	{
		TargetId = "root-0001",
		Format = "png",
		Width = 2,
		Height = 1,
		ByteCount = 4,
		BytesBase64 = "AQIDBA==",
	};

	public StandardIpcResponse ActionResponse { get; set; } = StandardIpcResponse.Ok();

	public bool Disposed { get; private set; }

	public TResponse Send<TResponse>(IpcCommand command, int timeoutMs)
	{
		if (SendException is not null)
			throw SendException;

		Commands.Add(command);
		object response = command switch
		{
			PingCommandRequest => new PingCommandResponse { ProcessId = Hello.ProcessId, IsWpfAvailable = true, RootCount = 1 },
			PipeStatusCommandRequest => new PipeStatusCommandResponse { PipeName = Hello.PipeName, IsReusable = true },
			GetVisualTreeCommandRequest => Snapshot,
			ScreenshotCommandRequest screenshot => new ScreenshotCommandResponse
			{
				TargetId = screenshot.TargetId ?? Screenshot.TargetId,
				Format = screenshot.Format,
				Width = Screenshot.Width,
				Height = Screenshot.Height,
				ByteCount = Screenshot.ByteCount,
				BytesBase64 = Screenshot.BytesBase64,
			},
			ClickCommandRequest => ActionResponse,
			FocusCommandRequest => ActionResponse,
			TypeTextCommandRequest => ActionResponse,
			KeyPressCommandRequest => ActionResponse,
			SetPropertyCommandRequest => ActionResponse,
			KnownRoutedEventCommandRequest => ActionResponse,
			KnownOperationCommandRequest => ActionResponse,
			InvokeCommandRequest => ActionResponse,
			StartSendingCommandRequest start => new StartSendingCommandResponse
			{
				SubscriptionId = "sub-1",
				StreamKind = start.StreamKind,
				Status = ProtocolConstants.Statuses.Started,
				IntervalMs = start.IntervalMs,
				SequenceStart = 1,
			},
			StopSendingCommandRequest stop => new StopSendingCommandResponse
			{
				SubscriptionId = stop.SubscriptionId,
				Status = ProtocolConstants.Statuses.Stopped,
			},
			_ => throw new InvalidOperationException("Unexpected command " + command.Kind),
		};
		return (TResponse)response;
	}

	public ICliStreamSession StartStream(StartSendingCommandRequest command, int timeoutMs)
	{
		Commands.Add(command);
		return new FakeCliStreamSession(new StartSendingCommandResponse
		{
			SubscriptionId = "sub-1",
			StreamKind = command.StreamKind,
			Status = ProtocolConstants.Statuses.Started,
			IntervalMs = command.IntervalMs,
			SequenceStart = 1,
		});
	}

	public void Dispose()
	{
		Disposed = true;
	}
}

internal sealed class FakeCliStreamSession : ICliStreamSession
{
	private int sequence;

	public FakeCliStreamSession(StartSendingCommandResponse start)
	{
		Start = start;
	}

	public StartSendingCommandResponse Start { get; }

	public StreamMessage? ReadFrame(int timeoutMs, CancellationToken cancellationToken = default)
	{
		if (sequence >= 3)
			return null;

		sequence++;
		return new StreamMessage
		{
			SubscriptionId = Start.SubscriptionId,
			StreamKind = Start.StreamKind,
			SequenceNumber = sequence,
			Data = new { status = "fake" },
		};
	}

	public void Dispose()
	{
	}
}

internal sealed class FakeTargetProcess : ITargetProcess
{
	public int Id { get; set; } = 1234;

	public string ProcessName { get; set; } = "TestTarget";

	public bool HasExited { get; set; }

	public bool Killed { get; private set; }

	public bool Disposed { get; private set; }

	public void Kill()
	{
		Killed = true;
	}

	public void Dispose()
	{
		Disposed = true;
	}
}
