namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class NodePropsSelectorsCommandHandlerTests
{
	[Test]
	public void NodeCommandReturnsNodeAndContext()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "node", "--pid", "1234", "--target", "0002", "--include-ancestors", "--include-path" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"targetId\":\"button-0002\""));
		Assert.That(result.Stdout, Does.Contain("\"ancestors\""));
	}

	[Test]
	public void PropsCommandReturnsSelectedProperties()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "props", "--pid", "1234", "--target-id", "button-0002", "--props", "Text" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"Text\":\"Submit\""));
		Assert.That(result.Stdout, Does.Not.Contain("\"IsEnabled\""));
	}

	[Test]
	public void NodeReportsStaleTarget()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "node", "--pid", "1234", "--target", "missing" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(8));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"stale-target\""));
	}

	[Test]
	public void SelectorsPreferAutomationIdAndIncludeFallbacks()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "selectors", "--pid", "1234", "--target", "0002" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("--automation-id"));
		Assert.That(result.Stdout, Does.Contain("--target"));
	}
}
