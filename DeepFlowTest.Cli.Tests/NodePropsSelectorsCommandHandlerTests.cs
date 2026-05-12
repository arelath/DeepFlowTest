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
	public void NodeAcceptsCompatContextAliases()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "node", "--pid", "1234", "--target", "root-0001", "--ancestors", "--children", "--subtree", "--subtree-depth", "1" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"children\""));
		Assert.That(result.Stdout, Does.Contain("\"subtree\""));
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
	public void NodeReportsAmbiguousShortTargetAndDepthZeroSubtreeIsEmpty()
	{
		var session = new FakeAppSessionService();
		session.Session.Snapshot.Nodes.Add(new DeepFlowTest.Interop.VisualTreeNodeDto
		{
			TargetId = "other-0002",
			IsRoot = true,
			Properties = new System.Collections.Generic.Dictionary<string, object?> { ["IsVisible"] = true },
		});
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var ambiguous = CliTestHost.Run(new[] { "node", "--pid", "1234", "--target", "0002" }, services);
		var subtree = CliTestHost.Run(new[] { "node", "--pid", "1234", "--target", "root-0001", "--include-subtree", "--subtree-depth", "0" }, services);

		Assert.That(ambiguous.ExitCode, Is.EqualTo(2));
		Assert.That(ambiguous.Stdout, Does.Contain("\"code\":\"ambiguous-target\""));
		Assert.That(subtree.Stdout, Does.Contain("\"subtree\":[]"));
	}

	[Test]
	public void NodePropsAndSelectorsHaveTextOutput()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var node = CliTestHost.Run(new[] { "node", "--pid", "1234", "--target", "0002", "--format", "text" }, services);
		var props = CliTestHost.Run(new[] { "props", "--pid", "1234", "--target", "0002", "--format", "text" }, services);
		var selectors = CliTestHost.Run(new[] { "selectors", "--pid", "1234", "--target", "0002", "--format", "text" }, services);

		Assert.That(node.Stdout, Does.Contain("button-0002"));
		Assert.That(props.Stdout, Does.Contain("Text: Submit"));
		Assert.That(selectors.Stdout, Does.Contain("--automation-id"));
	}

	[Test]
	public void SelectorsPreferAutomationIdAndIncludeFallbacks()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "selectors", "--pid", "1234", "--target", "0002" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("--automation-id"));
		Assert.That(result.Stdout, Does.Contain("--target"));
		Assert.That(result.Stdout, Does.Not.Contain("--path"));
	}

	[Test]
	public void SelectorsHandleTextOnlyStaleAndAmbiguousTargets()
	{
		var session = new FakeAppSessionService();
		session.Session.Snapshot.Nodes.Add(new DeepFlowTest.Interop.VisualTreeNodeDto
		{
			TargetId = "text-0003",
			IsRoot = true,
			Properties = new System.Collections.Generic.Dictionary<string, object?>
			{
				["Text"] = "Only \"Text\" \\ Value",
				["IsVisible"] = true,
			},
		});
		session.Session.Snapshot.Nodes.Add(new DeepFlowTest.Interop.VisualTreeNodeDto
		{
			TargetId = "other-0002",
			IsRoot = true,
			Properties = new System.Collections.Generic.Dictionary<string, object?> { ["IsVisible"] = true },
		});
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var textOnly = CliTestHost.Run(new[] { "selectors", "--pid", "1234", "--target", "0003" }, services);
		var stale = CliTestHost.Run(new[] { "selectors", "--pid", "1234", "--target", "missing" }, services);
		var ambiguous = CliTestHost.Run(new[] { "selectors", "--pid", "1234", "--target", "0002" }, services);

		Assert.That(textOnly.ExitCode, Is.EqualTo(0));
		Assert.That(textOnly.Stdout, Does.Contain("--text"));
		Assert.That(textOnly.Stdout, Does.Contain("\\\\\\\\ Value"));
		Assert.That(stale.ExitCode, Is.EqualTo(8));
		Assert.That(ambiguous.ExitCode, Is.EqualTo(2));
	}
}
