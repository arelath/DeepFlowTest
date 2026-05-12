namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class WaitCommandHandlerTests
{
	[Test]
	public void WaitReturnsImmediateSuccess()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "wait", "--pid", "1234", "--automation-id", "SubmitButton" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"matchCount\":1"));
	}

	[Test]
	public void WaitTimeoutMapsToCommandTimeout()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "wait", "--pid", "1234", "--name", "Missing", "--timeout-ms", "1", "--interval-ms", "1" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(6));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"command-timeout\""));
	}
}
