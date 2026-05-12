namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class ProtocolCommandHandlerTests
{
	[Test]
	public void PingUsesFakeSessionAndReturnsMetadata()
	{
		var resolver = new FakeTargetResolver();
		var sessionService = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: resolver, appSessionService: sessionService);

		var result = CliTestHost.Run(new[] { "ping", "--pid", "1234" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"rootCount\":1"));
		Assert.That(sessionService.Session.Disposed, Is.True);
		Assert.That(resolver.LastSelector!.ProcessId, Is.EqualTo(1234));
	}

	[Test]
	public void PipeStatusUsesFakeSession()
	{
		var sessionService = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: sessionService);

		var result = CliTestHost.Run(new[] { "pipe", "status", "--pid", "1234" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"pipeName\":\"fake-pipe\""));
	}

	[Test]
	public void DebugProgressGoesToStderr()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "ping", "--pid", "1234", "--debug" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stderr, Does.Contain("Connected to"));
	}
}
