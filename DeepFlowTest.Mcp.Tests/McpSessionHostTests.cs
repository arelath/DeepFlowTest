namespace DeepFlowTest.Mcp.Tests;

using DeepFlowTest.Cli;
using DeepFlowTest.Mcp.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class McpSessionHostTests
{
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

		var ex = Assert.Throws<CliException>(() => fixture.Host.Attach(new McpTargetSelector()));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.InvalidArguments));
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
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.TargetExited));
	}

	[Test]
	public void LaunchRequiresPolicyBeforeStartingProcess()
	{
		var launcher = new FakeProcessLauncher();
		var fixture = McpTestHost.CreateHost(options: McpTestHost.Options(allowLaunch: false), launcher: launcher);

		var ex = Assert.Throws<CliException>(() => fixture.Host.Launch(new McpLaunchOptions { FileName = "Harness.exe" }));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.ActionDenied));
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

		var ex = Assert.Throws<CliException>(() => fixture.Host.Launch(new McpLaunchOptions
		{
			FileName = "Harness.exe",
			TerminateOnDetach = true,
		}));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.TargetExited));
		Assert.That(launcher.Process.Killed, Is.False);
		Assert.That(launcher.Process.Disposed, Is.True);
	}

	[Test]
	public void LaunchAttachFailureCleansUpStartedProcess()
	{
		var launcher = new FakeProcessLauncher();
		var sessionService = new FakeAppSessionService
		{
			OpenException = new CliException(CliErrorCodes.AttachFailed, "attach failed"),
		};
		var fixture = McpTestHost.CreateHost(
			options: McpTestHost.Options(allowLaunch: true),
			sessionService: sessionService,
			launcher: launcher);

		var ex = Assert.Throws<CliException>(() => fixture.Host.Launch(new McpLaunchOptions
		{
			FileName = "Harness.exe",
			TerminateOnDetach = true,
		}));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.AttachFailed));
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
			OpenException = new CliException(CliErrorCodes.AttachFailed, "attach failed"),
		};
		var fixture = McpTestHost.CreateHost(
			options: McpTestHost.Options(allowLaunch: true),
			sessionService: sessionService,
			launcher: launcher);

		Assert.Throws<CliException>(() => fixture.Host.Launch(new McpLaunchOptions
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
