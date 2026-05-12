namespace DeepFlowTest.Tests;

using System;
using DeepFlowTest;
using DeepFlowTest.Tests.Fakes;
using NUnit.Framework;

[TestFixture]
public sealed class AppConnectionTests
{
	[Test]
	public void LaunchOwnedDisposalKillsOwnedProcess()
	{
		var process = new FakeTargetProcess();
		using var connection = AppConnection.ForLaunch(process, "pipe-launch", "dotnet");

		connection.Dispose();

		Assert.That(process.KillCount, Is.EqualTo(1));
		Assert.That(process.DisposeCount, Is.EqualTo(1));
		Assert.That(connection.IsDisposed, Is.True);
	}

	[Test]
	public void AttachDisposalLeavesTargetAlive()
	{
		var process = new FakeTargetProcess();
		using var connection = AppConnection.ForAttach(process, "pipe-attach", "dotnet");

		connection.Dispose();

		Assert.That(process.KillCount, Is.EqualTo(0));
		Assert.That(process.DisposeCount, Is.EqualTo(1));
	}

	[Test]
	public void ReusePathDoesNotReinjectWhenPipeIsAvailable()
	{
		var injector = new FakeInjector();
		using var connection = AppConnection.ForAttach(new FakeTargetProcess(), "pipe-reuse", "dotnet");

		connection.EnsurePipeOrInject(_ => true, injector, allowInjection: true);

		Assert.That(connection.ReusesPipe, Is.True);
		Assert.That(connection.InjectorState, Is.EqualTo(AppConnectionInjectorState.InjectionSkipped));
		Assert.That(injector.InjectCount, Is.EqualTo(0));
	}

	[Test]
	public void UnavailablePipeInjectsWhenPolicyAllows()
	{
		var injector = new FakeInjector
		{
			Result = new AppConnectionInjectionResult
			{
				PayloadFrameworkFamily = "net-framework",
				StartupLogTail = "started",
			},
		};
		using var connection = AppConnection.ForAttach(new FakeTargetProcess(), "pipe-inject", "dotnet");

		connection.EnsurePipeOrInject(_ => false, injector, allowInjection: true);

		Assert.That(connection.ReusesPipe, Is.False);
		Assert.That(connection.InjectorState, Is.EqualTo(AppConnectionInjectorState.Injected));
		Assert.That(connection.PayloadFrameworkFamily, Is.EqualTo("net-framework"));
		Assert.That(connection.LastStartupLog, Is.EqualTo("started"));
		Assert.That(injector.InjectCount, Is.EqualTo(1));
	}

	[Test]
	public void InjectionFailureCollectsStartupLog()
	{
		var injector = new FakeInjector
		{
			ThrowOnInject = true,
			StartupLog = "payload crash tail",
		};
		using var connection = AppConnection.ForAttach(new FakeTargetProcess(), "pipe-failure", "dotnet");

		var exception = Assert.Throws<AppConnectionException>(() => connection.EnsurePipeOrInject(_ => false, injector, allowInjection: true));

		Assert.That(connection.InjectorState, Is.EqualTo(AppConnectionInjectorState.Failed));
		Assert.That(connection.LastStartupLog, Is.EqualTo("payload crash tail"));
		Assert.That(exception!.StartupLogTail, Is.EqualTo("payload crash tail"));
	}

	private sealed class FakeInjector : IAppConnectionInjector
	{
		public int InjectCount { get; private set; }

		public bool ThrowOnInject { get; set; }

		public string? StartupLog { get; set; }

		public AppConnectionInjectionResult Result { get; set; } = new();

		public AppConnectionInjectionResult Inject(AppConnection connection)
		{
			InjectCount++;
			if (ThrowOnInject)
				throw new InvalidOperationException("inject failed");

			return Result;
		}

		public string? TryReadStartupLog(AppConnection connection) => StartupLog;
	}
}
