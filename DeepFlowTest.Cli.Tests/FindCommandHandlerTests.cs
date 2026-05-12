namespace DeepFlowTest.Cli.Tests;

using System.Linq;
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
	public void FindAcceptsCompatPropertyAliases()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var exact = CliTestHost.Run(new[] { "find", "--pid", "1234", "--prop", "Name=SubmitButton" }, services);
		var contains = CliTestHost.Run(new[] { "find", "--pid", "1234", "--contains", "Text=Sub" }, services);
		var regex = CliTestHost.Run(new[] { "find", "--pid", "1234", "--regex", "Name=^Submit" }, services);

		Assert.That(exact.ExitCode, Is.EqualTo(0));
		Assert.That(contains.ExitCode, Is.EqualTo(0));
		Assert.That(regex.ExitCode, Is.EqualTo(0));
		Assert.That(exact.Stdout, Does.Contain("\"matchCount\":1"));
		Assert.That(contains.Stdout, Does.Contain("\"matchCount\":1"));
		Assert.That(regex.Stdout, Does.Contain("\"matchCount\":1"));
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

	[Test]
	public void FindLimitCanIncreaseSnapshotRequestLimit()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "find", "--pid", "1234", "--automation-id", "SubmitButton", "--limit", "1500" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(session.Session.Commands.OfType<DeepFlowTest.Contracts.GetVisualTreeCommandRequest>().Single().MaxNodeCount, Is.EqualTo(1500));
	}
}
