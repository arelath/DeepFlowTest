namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Tests.Fakes;
using NUnit.Framework;
using FakeSession = DeepFlowTest.Tests.Fakes.FakeAppDriverCommandSession;

[TestFixture]
public sealed class AppDriverTests
{
	[Test]
	public void LaunchUsesBackendAndOwnsProcessByDefault()
	{
		var backend = new FakeBackend();
		var factory = new AppDriverFactory(backend);

		using var driver = factory.Launch("target.exe", new AppDriverLaunchOptions { Arguments = "--demo" });

		Assert.That(backend.LaunchedExecutablePath, Is.EqualTo("target.exe"));
		Assert.That(backend.LaunchedOptions!.Arguments, Is.EqualTo("--demo"));
		Assert.That(driver.Connection.OwnsProcess, Is.True);
		driver.Dispose();
		Assert.That(((FakeTargetProcess)driver.Connection.TargetProcess).KillCount, Is.EqualTo(1));
	}

	[Test]
	public void AttachByPidUsesBackendAndDoesNotOwnProcess()
	{
		var backend = new FakeBackend();
		var factory = new AppDriverFactory(backend);

		using var driver = factory.AttachTo(42);

		Assert.That(backend.AttachedProcessId, Is.EqualTo(42));
		Assert.That(driver.Connection.OwnsProcess, Is.False);
		driver.Dispose();
		Assert.That(((FakeTargetProcess)driver.Connection.TargetProcess).KillCount, Is.EqualTo(0));
	}

	[Test]
	public void ProcessNameResolutionUsesExactThenContainsAndRejectsAmbiguity()
	{
		var processes = new ITargetProcess[]
		{
			new FakeTargetProcess { Id = 1, ProcessName = "SampleApp" },
			new FakeTargetProcess { Id = 2, ProcessName = "OtherSampleApp" },
			new FakeTargetProcess { Id = 3, ProcessName = "Exact" },
		};

		Assert.That(AppDriverProcessResolver.ResolveByName(processes, "Exact").Id, Is.EqualTo(3));
		Assert.That(AppDriverProcessResolver.ResolveByName(processes, "Other").Id, Is.EqualTo(2));
		Assert.That(
			() => AppDriverProcessResolver.ResolveByName(processes, "Sample"),
			Throws.TypeOf<AppDriverException>().With.Property(nameof(AppDriverException.ErrorCode)).EqualTo(AppDriverErrorCodes.AmbiguousTarget));
		Assert.That(
			() => AppDriverProcessResolver.ResolveByName(processes, "Missing"),
			Throws.TypeOf<AppDriverException>().With.Property(nameof(AppDriverException.ErrorCode)).EqualTo(AppDriverErrorCodes.TargetNotFound));
	}

	[Test]
	public void DefaultBackendAttachByNameCreatesAttachConnectionWithoutInjectionWhenPolicyDisallows()
	{
		var process = new FakeTargetProcess { Id = 99, ProcessName = "Harness" };
		var backend = new DefaultAppDriverBackend(new FakeProcessCatalog(new[] { process }));

		using var connection = backend.AttachTo("Harness", new AppDriverAttachOptions
		{
			AllowInjection = false,
			PipeName = "deepflowtest-test-pipe",
		});

		Assert.That(connection.TargetProcess.Id, Is.EqualTo(99));
		Assert.That(connection.OwnsProcess, Is.False);
		Assert.That(connection.InjectorState, Is.EqualTo(AppConnectionInjectorState.InjectionSkipped));
	}

	[Test]
	public void TimeoutOptionsExposeDeterministicElementPollingBackoff()
	{
		var options = new AppDriverAttachOptions();

		Assert.That(options.ElementPollBackoffMs, Is.EqualTo(new[] { 25, 100, 500, 1000, 2000 }));
		options.Timeout = TimeSpan.FromMilliseconds(1234);
		Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1234)));
	}

	[Test]
	public void GetElementPollsWithConfiguredBackoff()
	{
		var session = new FakeSession(
			new FindElementCommandResponse { Status = ProtocolConstants.Statuses.NoMatch },
			new FindElementCommandResponse
			{
				Matches = { new FindElementMatchResponse { TargetId = "target", TypeName = "Button" } },
				MatchCount = 1,
			});
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session,
			new AppDriverOptions { ElementPollBackoffMs = new[] { 1 }, Timeout = TimeSpan.FromSeconds(1) });

		var element = driver.GetElement(ElementSelector.ByName("late"));

		Assert.That(element.TargetId, Is.EqualTo("target"));
		Assert.That(session.SentCommands.Count, Is.EqualTo(2));
	}

	[Test]
	public void DefaultInjectorLauncherPathUsesArchitectureResourceLayoutWhenPresent()
	{
		var architecture = Environment.Is64BitProcess ? "x64" : "x86";
		var resourceDirectory = Path.Combine(AppContext.BaseDirectory, "DeepFlowTestResources", architecture);
		var launcherPath = Path.Combine(resourceDirectory, $"DeepFlowTest.InjectorLauncher.{architecture}.exe");
		var created = false;
		if (!File.Exists(launcherPath))
		{
			Directory.CreateDirectory(resourceDirectory);
			File.WriteAllText(launcherPath, string.Empty);
			created = true;
		}
		try
		{
			var options = new AppDriverOptions();

			Assert.That(options.InjectorLauncherPath, Is.EqualTo(launcherPath));
		}
		finally
		{
			if (created)
				File.Delete(launcherPath);
		}
	}

	private sealed class FakeBackend : IAppDriverBackend
	{
		public string? LaunchedExecutablePath { get; private set; }

		public AppDriverLaunchOptions? LaunchedOptions { get; private set; }

		public int? AttachedProcessId { get; private set; }

		public AppConnection Launch(string executablePath, AppDriverLaunchOptions options)
		{
			LaunchedExecutablePath = executablePath;
			LaunchedOptions = options;
			return AppConnection.ForLaunch(new FakeTargetProcess(), options.PipeName ?? "launch-pipe");
		}

		public AppConnection AttachTo(int processId, AppDriverAttachOptions options)
		{
			AttachedProcessId = processId;
			return AppConnection.ForAttach(new FakeTargetProcess { Id = processId }, options.PipeName ?? "attach-pipe");
		}

		public AppConnection AttachTo(string processName, AppDriverAttachOptions options)
		{
			return AppConnection.ForAttach(new FakeTargetProcess { ProcessName = processName }, options.PipeName ?? "attach-name-pipe");
		}
	}

	private sealed class FakeProcessCatalog : IProcessCatalog
	{
		private readonly IReadOnlyList<ITargetProcess> processes;

		public FakeProcessCatalog(IReadOnlyList<ITargetProcess> processes)
		{
			this.processes = processes;
		}

		public ITargetProcess GetById(int processId)
		{
			foreach (var process in processes)
				if (process.Id == processId)
					return process;

			throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, "missing");
		}

		public IReadOnlyList<ITargetProcess> GetProcesses() => processes;
	}

}
