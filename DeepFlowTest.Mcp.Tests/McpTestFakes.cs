namespace DeepFlowTest.Mcp.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DeepFlowTest;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class McpTestHost
{
	public static McpServerOptions Options(bool allowLaunch = false, bool allowActions = false, bool allowFileWrites = false) =>
		new()
		{
			DefaultTimeoutMs = 500,
			AttachTimeoutMs = 500,
			CacheTtlMs = 500,
			TreeLimit = 100,
			Policy = new McpPolicyOptions
			{
				AllowLaunch = allowLaunch,
				AllowActions = allowActions,
				AllowFileWrites = allowFileWrites,
			},
		};

	public static HostFixture CreateHost(
		McpServerOptions? options = null,
		ITargetResolver? resolver = null,
		ICliAppSessionService? sessionService = null,
		IMcpProcessLauncher? launcher = null,
		IProcessSnapshotSource? snapshotSource = null)
	{
		var optionSource = Microsoft.Extensions.Options.Options.Create(options ?? Options());
		var services = new CliServices(
			new CliDefaultsStore(CreateTempConfigPath()),
			snapshotSource ?? new FakeProcessSnapshotSource(),
			resolver ?? new FakeTargetResolver(),
			sessionService ?? new FakeAppSessionService());
		var cache = new McpSnapshotCache(optionSource);
		var streams = new McpStreamRegistry(optionSource);
		var handles = new McpElementHandleRegistry();
		var activity = new McpActivityStore(optionSource);
		var resources = new DeepFlowResourceStore(optionSource, activity);
		var factory = new McpTargetSessionFactory(services, launcher ?? new FakeProcessLauncher(), optionSource);
		var host = new McpSessionHost(factory, cache, streams, activity, optionSource, handles);
		var runner = new McpToolRunner(host, resources, NullLogger<McpToolRunner>.Instance, activity);
		var provider = new ServiceCollection()
			.AddSingleton(host)
			.AddSingleton(cache)
			.AddSingleton(streams)
			.AddSingleton(handles)
			.AddSingleton(resources)
			.AddSingleton(activity)
			.AddSingleton(services)
			.AddSingleton<IOptions<McpServerOptions>>(optionSource)
			.BuildServiceProvider();
		return new HostFixture(host, runner, cache, streams, handles, resources, services, optionSource, provider);
	}

	public static VisualTreeSnapshot Snapshot() =>
		VisualTreeSnapshot.Create(1, new[]
		{
			Node("root-0001", isRoot: true, childIds: ["button-0002"]),
			Node("button-0002", parentId: "root-0001", type: "Button", text: "Submit"),
		});

	public static VisualTreeNodeDto Node(
		string id,
		string? parentId = null,
		bool isRoot = false,
		IReadOnlyList<string>? childIds = null,
		string type = "Window",
		string text = "Root") =>
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
				[KnownProperties.Name] = id,
				[KnownProperties.AutomationId] = type == "Button" ? "SubmitButton" : id,
				[KnownProperties.Text] = text,
				[KnownProperties.Content] = text,
				[KnownProperties.IsVisible] = true,
				[KnownProperties.IsEnabled] = true,
			},
		};

	private static string CreateTempConfigPath() =>
		System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DeepFlowTest.Mcp.Tests", Guid.NewGuid().ToString("N"), "defaults.json");
}

internal sealed record class HostFixture(
	McpSessionHost Host,
	McpToolRunner Runner,
	McpSnapshotCache Cache,
	McpStreamRegistry Streams,
	McpElementHandleRegistry Handles,
	DeepFlowResourceStore Resources,
	CliServices Services,
	IOptions<McpServerOptions> Options,
	ServiceProvider ServiceProvider) : IDisposable
{
	public void Dispose()
	{
		ServiceProvider.Dispose();
		Host.Dispose();
	}
}

internal sealed class FakeTargetResolver : ITargetResolver
{
	public TargetSelector? LastSelector { get; private set; }

	public TargetInfo Target { get; set; } = new()
	{
		ProcessId = 1234,
		ProcessName = "Harness",
		MainWindowTitle = "Harness Window",
		Architecture = "x64",
		FrameworkFamily = "wpf",
		TargetProcess = new FakeTargetProcess(1234, "Harness"),
	};

	public TargetInfo Resolve(TargetSelector selector)
	{
		LastSelector = selector;
		return Target;
	}
}

internal sealed class FakeAppSessionService : ICliAppSessionService
{
	public FakeAppSession Session { get; } = new();

	public Queue<FakeAppSession> PendingSessions { get; } = new();

	public int OpenCount { get; private set; }

	public TargetInfo? LastTarget { get; private set; }

	public CliException? OpenException { get; set; }

	public ICliAppSession Open(TargetInfo target, CliAttachOptions options)
	{
		OpenCount++;
		LastTarget = target;
		if (OpenException is not null)
			throw OpenException;

		return PendingSessions.Count > 0 ? PendingSessions.Dequeue() : Session;
	}
}

internal sealed class FakeAppSession : ICliAppSession
{
	public HelloCommandResponse Hello { get; } = new()
	{
		ProtocolVersion = ProtocolConstants.ProtocolVersion,
		PipeName = "fake-pipe",
		ProcessId = 1234,
		FrameworkFamily = "wpf",
	};

	public List<IpcCommand> Commands { get; } = [];

	public bool Disposed { get; private set; }

	public VisualTreeSnapshot Snapshot { get; set; } = McpTestHost.Snapshot();

	public IReadOnlyList<StreamMessage> StreamFrames { get; set; } =
	[
		new StreamMessage("sub-1", ProtocolConstants.StreamKinds.VisualTree, 1, new { ok = true }),
	];

	public Func<IpcCommand, object>? SendHandler { get; set; }

	public Func<StartSendingCommandRequest, int, ICliStreamSession>? StartStreamHandler { get; set; }

	public FakeCliStreamSession? LastStreamSession { get; private set; }

	public TResponse Send<TResponse>(IpcCommand command, int timeoutMs)
	{
		Commands.Add(command);
		object response = SendHandler?.Invoke(command) ?? command switch
		{
			PingCommandRequest => new PingCommandResponse(1, Snapshot.NodeCount),
			GetVisualTreeCommandRequest => Snapshot,
			GetBindingFailuresCommandRequest => new BindingFailureBatchDto(),
			ScreenshotCommandRequest screenshot => new ScreenshotCommandResponse
			{
				TargetId = screenshot.TargetId ?? string.Empty,
				Format = screenshot.Format,
				Width = 2,
				Height = 2,
				ByteCount = 4,
				BytesBase64 = "AQIDBA==",
			},
			StopSendingCommandRequest stop => new StopSendingCommandResponse(stop.SubscriptionId, ProtocolConstants.Statuses.Stopped),
			DragAndDropCommandRequest => new StandardIpcResponse { Success = true },
			_ => new StandardIpcResponse { Success = true },
		};
		return (TResponse)response;
	}

	public ICliStreamSession StartStream(StartSendingCommandRequest command, int timeoutMs)
	{
		Commands.Add(command);
		if (StartStreamHandler is not null)
			return StartStreamHandler(command, timeoutMs);

		LastStreamSession = new FakeCliStreamSession(command.StreamKind, StreamFrames);
		return LastStreamSession;
	}

	public void Dispose()
	{
		Disposed = true;
	}
}

internal sealed class FakeCliStreamSession : ICliStreamSession
{
	private readonly Queue<StreamMessage> frames;

	public FakeCliStreamSession(string streamKind, IReadOnlyList<StreamMessage> frames)
	{
		Start = new StartSendingCommandResponse("sub-1", streamKind, ProtocolConstants.Statuses.Started);
		this.frames = new Queue<StreamMessage>(frames);
	}

	public StartSendingCommandResponse Start { get; }

	public int ReadCount { get; private set; }

	public bool Disposed { get; private set; }

	public StreamMessage? ReadFrame(int timeoutMs, CancellationToken cancellationToken = default)
	{
		ReadCount++;
		if (frames.Count == 0)
			return null;

		return frames.Dequeue();
	}

	public void Dispose()
	{
		Disposed = true;
	}
}

internal sealed class FakeProcessSnapshotSource : IProcessSnapshotSource
{
	public ProcessSnapshotResult Result { get; set; } = new()
	{
		Processes =
		[
			new ProcessSnapshot
			{
				ProcessId = 1,
				ProcessName = "UiApp",
				MainWindowTitle = "UI",
				TopLevelWindows = [new ProcessWindowSnapshot { Hwnd = 100, Title = "UI" }],
				IsLikelyWpfCandidate = true,
			},
			new ProcessSnapshot
			{
				ProcessId = 2,
				ProcessName = "Worker",
				IsLikelyWpfCandidate = false,
			},
		],
	};

	public ProcessSnapshotResult GetSnapshots() => Result;
}

internal sealed class FakeProcessLauncher : IMcpProcessLauncher
{
	public FakeTargetProcess Process { get; set; } = new(4321, "Harness");

	public int StartCount { get; private set; }

	public IMcpLaunchedProcess Start(DeepFlowTest.Mcp.Contracts.McpLaunchOptions options)
	{
		StartCount++;
		return Process;
	}
}

internal sealed class FakeTargetProcess : IMcpLaunchedProcess
{
	public FakeTargetProcess(int id, string processName)
	{
		Id = id;
		ProcessName = processName;
	}

	public int Id { get; }

	public string ProcessName { get; }

	public string? MainWindowTitle { get; set; } = "Harness Window";

	public bool HasExited { get; set; }

	public int? ExitCode { get; set; }

	public bool Killed { get; private set; }

	public bool Disposed { get; private set; }

	public void Refresh()
	{
	}

	public void Kill()
	{
		Killed = true;
		HasExited = true;
	}

	public void Kill(bool entireProcessTree) => Kill();

	public void Dispose()
	{
		Disposed = true;
	}
}
