namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class FindCommandHandlerTests
{
	[Test]
	public void FindByCommonSelectorsAgainstSnapshot()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "find", "--pid", "1234", "--automation-id", "SubmitButton", "--include-path", "--include-properties" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"matchCount\":1"));
		Assert.That(result.Stdout, Does.Contain("SubmitButton"));
	}

	[Test]
	public void FindNoMatchSucceedsUnlessRequired()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var optional = CliTestHost.Run(new[] { "find", "--pid", "1234", "--name", "Missing" }, services);
		var required = CliTestHost.Run(new[] { "find", "--pid", "1234", "--name", "Missing", "--require-match" }, services);

		Assert.That(optional.ExitCode, Is.EqualTo(0));
		Assert.That(optional.Stdout, Does.Contain("\"matchCount\":0"));
		Assert.That(required.ExitCode, Is.EqualTo(8));
		Assert.That(required.Stdout, Does.Contain("\"code\":\"no-match\""));
	}
}
