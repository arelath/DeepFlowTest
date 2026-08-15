namespace DeepFlowTest.Cli.Tests;

using System.Collections.Generic;
using DeepFlowTest.Contracts;
using ElementSelector = DeepFlowTest.Automation.ElementSelector;
using NUnit.Framework;

[TestFixture]
public sealed class ElementResolverTests
{
	[Test]
	public void ResolvesByTargetId()
	{
		var snapshot = Snapshot();

		var result = new ElementResolver().Resolve(snapshot, new ElementSelector { TargetId = "0002" });

		Assert.That(result.TargetId, Is.EqualTo("button-0002"));
	}

	[Test]
	public void ResolvesBySelectors()
	{
		var snapshot = Snapshot();
		var resolver = new ElementResolver();

		Assert.That(resolver.Resolve(snapshot, new ElementSelector { TypeName = "Button" }).TargetId, Is.EqualTo("button-0002"));
		Assert.That(resolver.Resolve(snapshot, new ElementSelector { Name = "button-0002" }).TargetId, Is.EqualTo("button-0002"));
		Assert.That(resolver.Resolve(snapshot, new ElementSelector { AutomationId = "SubmitButton" }).TargetId, Is.EqualTo("button-0002"));
		Assert.That(resolver.Resolve(snapshot, new ElementSelector { Text = "Submit" }).TargetId, Is.EqualTo("button-0002"));
		Assert.That(resolver.Resolve(snapshot, new ElementSelector { PropertyContains = new KeyValuePair<string, string>(KnownProperties.Text, "Sub") }).TargetId, Is.EqualTo("button-0002"));
	}

	[Test]
	public void SupportsFirstAndIndexSelection()
	{
		var snapshot = CliReadServiceTests.Snapshot(
			CliReadServiceTests.Node("root-1", isRoot: true, childIds: new[] { "button-1", "button-2" }),
			CliReadServiceTests.Node("button-1", "root-1", type: "Button", text: "One"),
			CliReadServiceTests.Node("button-2", "root-1", type: "Button", text: "Two"));
		var resolver = new ElementResolver();

		Assert.That(resolver.Resolve(snapshot, new ElementSelector { TypeName = "Button", First = true }).TargetId, Is.EqualTo("button-1"));
		Assert.That(resolver.Resolve(snapshot, new ElementSelector { TypeName = "Button", Index = 1 }).TargetId, Is.EqualTo("button-2"));
	}

	[Test]
	public void ReportsNoMatchAmbiguousAndInvalidRegex()
	{
		var snapshot = Snapshot();
		var resolver = new ElementResolver();

		Assert.That(
			() => resolver.Resolve(snapshot, new ElementSelector { Name = "Missing" }),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.NoMatch));
		Assert.That(
			() => resolver.Resolve(CliReadServiceTests.Snapshot(CliReadServiceTests.Node("a", isRoot: true, type: "Button"), CliReadServiceTests.Node("b", isRoot: true, type: "Button")), new ElementSelector { TypeName = "Button" }),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.AmbiguousTarget));
		Assert.That(
			() => resolver.Resolve(snapshot, new ElementSelector { PropertyRegex = new KeyValuePair<string, string>(KnownProperties.Text, "[") }),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.InvalidArguments));
	}

	private static DeepFlowTest.Interop.VisualTreeSnapshot Snapshot() =>
		CliReadServiceTests.Snapshot(
			CliReadServiceTests.Node("root-0001", isRoot: true, childIds: new[] { "button-0002" }),
			CliReadServiceTests.Node("button-0002", "root-0001", type: "Button", automationId: "SubmitButton", text: "Submit"));
}
