namespace DeepFlowTest.Cli.Tests;

using System.Linq;
using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class TreeCommandHandlerTests
{
	[Test]
	public void TreeReturnsDefaultJsonSnapshot()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "tree", "--pid", "1234" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"shape\":\"flat\""));
		Assert.That(result.Stdout, Does.Contain("SubmitButton"));
		Assert.That(session.Session.Commands.OfType<GetVisualTreeCommandRequest>().Single().PropNames, Does.Contain("IsVisible"));
	}

	[Test]
	public void TreeSupportsCustomPropertiesNestedShapeAndShortRoot()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "tree", "--pid", "1234", "--shape", "nested", "--root", "0002", "--props", "Text", "--include-path" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"shape\":\"nested\""));
		Assert.That(result.Stdout, Does.Contain("\"path\":\"/root-0001/button-0002\""));
		Assert.That(result.Stdout, Does.Contain("\"Text\":\"Submit\""));
	}

	[Test]
	public void TreeLimitIsUsedWhenRequestingSnapshotAndTextOutputIsReadable()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "tree", "--pid", "1234", "--limit", "1500", "--format", "text" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("shape: flat"));
		Assert.That(session.Session.Commands.OfType<GetVisualTreeCommandRequest>().Single().MaxNodeCount, Is.EqualTo(1500));
	}

	[Test]
	public void TreeSupportsCompatTypeNameFilterAndNoProperties()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "tree", "--pid", "1234", "--type-names", "Button", "--props", "none" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("button-0002"));
		Assert.That(result.Stdout, Does.Not.Contain("root-0001"));
		Assert.That(result.Stdout, Does.Contain("\"typeName\":\"Button\""));
		Assert.That(result.Stdout, Does.Not.Contain("SubmitButton"));
		Assert.That(session.Session.Commands.OfType<GetVisualTreeCommandRequest>().Single().PropNames, Is.EqualTo(new[] { "IsVisible" }));
	}
}
