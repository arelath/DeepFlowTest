namespace DeepFlowTest.Mcp.Tests;

using System.Linq;
using System.Threading;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

[TestFixture]
public sealed class McpSessionHostTests
{
	[Test]
	public void ExplicitContextsExpireAfterTheirIdleTimeout()
	{
		var options = McpTestHost.Options();
		options.ContextIdleTimeoutMs = 1;
		using var fixture = McpTestHost.CreateHost(options: options);
		var status = fixture.Host.AttachContext(new McpTargetSelector { ProcessId = 1234 });

		Thread.Sleep(20);
		var error = Assert.Throws<AutomationException>(() => fixture.Host.RequireContext(status.ContextId!));

		Assert.That(error!.ErrorCode, Is.EqualTo(AutomationErrorCodes.StaleTarget));
		Assert.That(((FakeAppSessionService)fixture.Services.SessionService).Session.DisposeCount, Is.EqualTo(1));
	}

	[Test]
	public void ExplicitContextCapturesPolicyAtCreation()
	{
		var options = McpTestHost.Options(allowActions: true);
		using var fixture = McpTestHost.CreateHost(options: options);
		var status = fixture.Host.AttachContext(new McpTargetSelector { ProcessId = 1234 });
		options.Policy.AllowActions = false;

		Assert.That(fixture.Host.GetContextPolicy(status.ContextId!).AllowActions, Is.True);
	}

	[Test]
	public void LegacyCurrentIsAnAliasToARegisteredContext()
	{
		using var fixture = McpTestHost.CreateHost();
		var status = fixture.Host.Attach(new McpTargetSelector { ProcessId = 1234 });

		var current = fixture.Host.Current;
		var byContext = fixture.Host.RequireContext(status.ContextId!);

		Assert.That(current, Is.SameAs(byContext));
		Assert.That(status.ContextId, Is.EqualTo(McpContextRegistry.ToContextId(current!.SessionId)));
	}

	[Test]
	public void DetachClosesOnlyTheSelectedLegacyContext()
	{
		var sessionService = new FakeAppSessionService();
		var explicitSession = new FakeAppSession();
		var legacySession = new FakeAppSession();
		sessionService.PendingSessions.Enqueue(explicitSession);
		sessionService.PendingSessions.Enqueue(legacySession);
		using var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		var explicitContextId = fixture.Host.AttachContext(new McpTargetSelector { ProcessId = 1234 }).ContextId!;
		fixture.Host.Attach(new McpTargetSelector { ProcessId = 1234 });

		fixture.Host.Detach();

		Assert.That(legacySession.Disposed, Is.True);
		Assert.That(explicitSession.Disposed, Is.False);
		Assert.That(fixture.Host.Current, Is.Null);
		Assert.That(fixture.Host.RequireContext(explicitContextId).AppSession, Is.SameAs(explicitSession));
	}

	[Test]
	public void ReplacingLegacySelectionDoesNotCloseExplicitContexts()
	{
		var sessionService = new FakeAppSessionService();
		var explicitSession = new FakeAppSession();
		var firstLegacySession = new FakeAppSession();
		var secondLegacySession = new FakeAppSession();
		sessionService.PendingSessions.Enqueue(explicitSession);
		sessionService.PendingSessions.Enqueue(firstLegacySession);
		sessionService.PendingSessions.Enqueue(secondLegacySession);
		using var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		var explicitContextId = fixture.Host.AttachContext(new McpTargetSelector { ProcessId = 1234 }).ContextId!;
		fixture.Host.Attach(new McpTargetSelector { ProcessId = 1234 });

		fixture.Host.Attach(new McpTargetSelector { ProcessId = 1234 });

		Assert.That(firstLegacySession.DisposeCount, Is.EqualTo(1));
		Assert.That(secondLegacySession.Disposed, Is.False);
		Assert.That(explicitSession.Disposed, Is.False);
		Assert.That(fixture.Host.RequireContext(explicitContextId).AppSession, Is.SameAs(explicitSession));
	}

	[Test]
	public void ExplicitCloseCleansStreamsSnapshotsHandlesAndSession()
	{
		var sessionService = new FakeAppSessionService();
		using var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		var contextId = fixture.Host.AttachContext(new McpTargetSelector { ProcessId = 1234 }).ContextId!;
		var session = fixture.Host.RequireContext(contextId);
		var snapshot = fixture.Cache.GetOrRefresh(session, [KnownProperties.Name], 100);
		var button = snapshot.Nodes.Single(node => node.TargetId == "button-0002");
		var buttonData = new TreeNodeData { TargetId = button.TargetId, TypeName = button.TypeName, Properties = button.Properties };
		fixture.Handles.Register(contextId, button.TargetId, new ElementSelector { TargetId = button.TargetId }, buttonData, snapshot.SequenceNumber);
		fixture.Streams.Start(session, new StartSendingCommandRequest(ProtocolConstants.StreamKinds.VisualTree, 10, timeoutMs: 500), 500);

		fixture.Host.CloseContext(contextId);

		Assert.That(sessionService.Session.LastStreamSession!.Disposed, Is.True);
		Assert.That(fixture.Cache.GetLatestRevision(session.SessionId), Is.Null);
		Assert.That(fixture.Handles.TryGetHandle(contextId, button.TargetId), Is.Null);
		Assert.That(sessionService.Session.DisposeCount, Is.EqualTo(1));
		Assert.That(
			() => fixture.Host.RequireContext(contextId),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.StaleTarget));
	}

	[Test]
	public void DeadExplicitContextRemainsTerminalUntilClosed()
	{
		var resolver = new FakeTargetResolver();
		var targetProcess = (FakeTargetProcess)resolver.Target.TargetProcess!;
		var sessionService = new FakeAppSessionService();
		using var fixture = McpTestHost.CreateHost(resolver: resolver, sessionService: sessionService);
		var contextId = fixture.Host.AttachContext(new McpTargetSelector { ProcessId = 1234 }).ContextId!;
		targetProcess.HasExited = true;
		targetProcess.ExitCode = 23;

		Assert.That(
			() => fixture.Host.RequireContext(contextId),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.TargetExited));
		Assert.That(
			() => fixture.Host.RequireContext(contextId),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.TargetExited));
		Assert.That(fixture.Host.GetContextStatus(contextId).ExitReason, Is.EqualTo("exited:23"));
		Assert.That(sessionService.Session.DisposeCount, Is.EqualTo(1));

		fixture.Host.CloseContext(contextId);
		Assert.That(
			() => fixture.Host.RequireContext(contextId),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.StaleTarget));
	}

	[Test]
	public void RegistryCloseIsIdempotentAndCleansOnce()
	{
		var sessionService = new FakeAppSessionService();
		using var fixture = McpTestHost.CreateHost(sessionService: sessionService);
		var contextId = fixture.Host.AttachContext(new McpTargetSelector { ProcessId = 1234 }).ContextId!;
		var registry = fixture.ServiceProvider.GetRequiredService<McpContextRegistry>();

		var first = registry.Close(contextId);
		var second = registry.Close(contextId);

		Assert.That(first.Closed, Is.True);
		Assert.That(second.Closed, Is.False);
		Assert.That(sessionService.Session.DisposeCount, Is.EqualTo(1));
	}

	[Test]
	public void AttachReplacesCurrentSessionAndDetachDisposesIt()
	{
		var sessionService = new FakeAppSessionService();
		var resolver = new FakeTargetResolver();
		var fixture = McpTestHost.CreateHost(resolver: resolver, sessionService: sessionService);

		var attached = fixture.Host.Attach(new McpTargetSelector { ProcessId = 1234 });
		var detached = fixture.Host.Detach();

		Assert.That(attached.Attached, Is.True);
		Assert.That(attached.ProcessId, Is.EqualTo(1234));
		Assert.That(detached.Attached, Is.False);
		Assert.That(sessionService.OpenCount, Is.EqualTo(1));
		Assert.That(sessionService.Session.Disposed, Is.True);
		Assert.That(resolver.LastSelector!.ProcessId, Is.EqualTo(1234));
	}

	[Test]
	public void AttachRequiresSelector()
	{
		var fixture = McpTestHost.CreateHost();

		var ex = Assert.Throws<AutomationException>(() => fixture.Host.Attach(new McpTargetSelector()));

		Assert.That(ex!.ErrorCode, Is.EqualTo(AutomationErrorCodes.InvalidArguments));
	}

	[Test]
	public void AttachSupportsProcessNameWindowTitleAndExecutablePathSelectors()
	{
		var resolver = new FakeTargetResolver();
		var fixture = McpTestHost.CreateHost(resolver: resolver);

		fixture.Host.Attach(new McpTargetSelector { ProcessName = "Harness.exe" });
		Assert.That(resolver.LastSelector!.ProcessName, Is.EqualTo("Harness.exe"));

		fixture.Host.Attach(new McpTargetSelector { WindowTitle = "Harness Window" });
		Assert.That(resolver.LastSelector!.WindowTitle, Is.EqualTo("Harness Window"));

		fixture.Host.Attach(new McpTargetSelector { ExecutablePath = @"C:\apps\Harness.exe" });
		Assert.That(resolver.LastSelector!.ProcessName, Is.EqualTo("Harness"));
	}

	[Test]
	public void StatusAndRequireSessionDetectDeadTargets()
	{
		var resolver = new FakeTargetResolver();
		var targetProcess = (FakeTargetProcess)resolver.Target.TargetProcess!;
		var fixture = McpTestHost.CreateHost(resolver: resolver);

		fixture.Host.Attach(new McpTargetSelector { ProcessId = 1234 });
		targetProcess.HasExited = true;
		targetProcess.ExitCode = 12;

		Assert.That(fixture.Host.Status.IsAlive, Is.False);
		Assert.That(fixture.Host.Status.ExitReason, Is.EqualTo("exited:12"));
		Assert.That(
			() => fixture.Host.RequireSession(),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.TargetExited));
	}

	[Test]
	public void LaunchRequiresPolicyBeforeStartingProcess()
	{
		var launcher = new FakeProcessLauncher();
		var fixture = McpTestHost.CreateHost(options: McpTestHost.Options(allowLaunch: false), launcher: launcher);

		var ex = Assert.Throws<AutomationException>(() => fixture.Host.Launch(new McpLaunchOptions { FileName = "Harness.exe" }));

		Assert.That(ex!.ErrorCode, Is.EqualTo(AutomationErrorCodes.ActionDenied));
		Assert.That(launcher.StartCount, Is.EqualTo(0));
	}

	[Test]
	public void LaunchStartsAndAttachesWhenPolicyAllowsIt()
	{
		var launcher = new FakeProcessLauncher();
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(
			options: McpTestHost.Options(allowLaunch: true),
			sessionService: sessionService,
			launcher: launcher);

		var status = fixture.Host.Launch(new McpLaunchOptions { FileName = "Harness.exe" });

		Assert.That(status.Attached, Is.True);
		Assert.That(status.LaunchedByServer, Is.True);
		Assert.That(launcher.StartCount, Is.EqualTo(1));
		Assert.That(sessionService.OpenCount, Is.EqualTo(1));
	}

	[Test]
	public void LaunchFailsWhenProcessExitsBeforeAttach()
	{
		var launcher = new FakeProcessLauncher
		{
			Process = new FakeTargetProcess(4321, "Harness")
			{
				HasExited = true,
				ExitCode = 42,
			},
		};
		var fixture = McpTestHost.CreateHost(options: McpTestHost.Options(allowLaunch: true), launcher: launcher);

		var ex = Assert.Throws<AutomationException>(() => fixture.Host.Launch(new McpLaunchOptions
		{
			FileName = "Harness.exe",
			TerminateOnDetach = true,
		}));

		Assert.That(ex!.ErrorCode, Is.EqualTo(AutomationErrorCodes.TargetExited));
		Assert.That(launcher.Process.Killed, Is.False);
		Assert.That(launcher.Process.Disposed, Is.True);
	}

	[Test]
	public void LaunchAttachFailureCleansUpStartedProcess()
	{
		var launcher = new FakeProcessLauncher();
		var sessionService = new FakeAppSessionService
		{
			OpenException = new AutomationException(AutomationErrorCodes.AttachFailed, "attach failed"),
		};
		var fixture = McpTestHost.CreateHost(
			options: McpTestHost.Options(allowLaunch: true),
			sessionService: sessionService,
			launcher: launcher);

		var ex = Assert.Throws<AutomationException>(() => fixture.Host.Launch(new McpLaunchOptions
		{
			FileName = "Harness.exe",
			TerminateOnDetach = true,
		}));

		Assert.That(ex!.ErrorCode, Is.EqualTo(AutomationErrorCodes.AttachFailed));
		Assert.That(sessionService.OpenCount, Is.EqualTo(1));
		Assert.That(launcher.Process.Killed, Is.True);
		Assert.That(launcher.Process.Disposed, Is.True);
	}

	[Test]
	public void LaunchAttachFailureDisposesProcessWhenTerminationIsNotRequested()
	{
		var launcher = new FakeProcessLauncher();
		var sessionService = new FakeAppSessionService
		{
			OpenException = new AutomationException(AutomationErrorCodes.AttachFailed, "attach failed"),
		};
		var fixture = McpTestHost.CreateHost(
			options: McpTestHost.Options(allowLaunch: true),
			sessionService: sessionService,
			launcher: launcher);

		Assert.Throws<AutomationException>(() => fixture.Host.Launch(new McpLaunchOptions
		{
			FileName = "Harness.exe",
			TerminateOnDetach = false,
		}));

		Assert.That(launcher.Process.Killed, Is.False);
		Assert.That(launcher.Process.Disposed, Is.True);
	}

	[Test]
	public void DetachTerminatesLaunchedProcessWhenRequested()
	{
		var launcher = new FakeProcessLauncher();
		var sessionService = new FakeAppSessionService();
		var fixture = McpTestHost.CreateHost(
			options: McpTestHost.Options(allowLaunch: true),
			sessionService: sessionService,
			launcher: launcher);

		fixture.Host.Launch(new McpLaunchOptions
		{
			FileName = "Harness.exe",
			TerminateOnDetach = true,
		});
		var status = fixture.Host.Detach();

		Assert.That(status.Attached, Is.False);
		Assert.That(launcher.Process.Killed, Is.True);
		Assert.That(sessionService.Session.Disposed, Is.True);
	}
}
