namespace DeepFlowTest.Cli.Tests;

using System.Linq;
using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class ActionCommandHandlerTests
{
	[Test]
	public void ClickSendsPayloadRequest()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "click", "--pid", "1234", "--target", "0002", "--button", "right" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		var command = session.Session.Commands.OfType<ClickCommandRequest>().Single();
		Assert.That(command.TargetId, Is.EqualTo("button-0002"));
		Assert.That(command.MouseButton, Is.EqualTo("right"));
	}

	[Test]
	public void DoubleClickUsesKnownRoutedEvent()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "click", "--pid", "1234", "--target", "0002", "--button", "double" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(session.Session.Commands.OfType<KnownRoutedEventCommandRequest>().Single().EventName, Is.EqualTo("MouseDoubleClick"));
	}

	[Test]
	public void ClickAcceptsCompatDoubleAliasAndSelectorAliases()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "click", "--pid", "1234", "--prop", "AutomationProperties.AutomationId=SubmitButton", "--require-visible", "--double" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		var command = session.Session.Commands.OfType<KnownRoutedEventCommandRequest>().Single();
		Assert.That(command.TargetId, Is.EqualTo("button-0002"));
		Assert.That(command.EventName, Is.EqualTo("MouseDoubleClick"));
	}

	[Test]
	public void RightDoubleClickKeepsClickPayload()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "click", "--pid", "1234", "--target", "0002", "--button", "right", "--count", "2" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		var command = session.Session.Commands.OfType<ClickCommandRequest>().Single();
		Assert.That(command.MouseButton, Is.EqualTo("right"));
		Assert.That(command.ClickCount, Is.EqualTo(2));
	}

	[Test]
	public void CommandSpecificDefaultsFeedActionHandlers()
	{
		var store = new CliDefaultsStore(CliTestHost.CreateTempConfigPath());
		store.Set("commands.click.button", "right");
		store.Set("commands.type.text", "from-default");
		store.Set("commands.type.clearFirst", "true");
		store.Set("commands.key.keys", "Enter");
		store.Set("commands.key.delayMs", "15");
		store.Set("commands.key.foreground", "false");
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(defaultsStore: store, targetResolver: new FakeTargetResolver(), appSessionService: session);

		Assert.That(CliTestHost.Run(new[] { "click", "--pid", "1234", "--target", "0002" }, services).ExitCode, Is.EqualTo(0));
		Assert.That(CliTestHost.Run(new[] { "type", "--pid", "1234", "--target", "0002" }, services).ExitCode, Is.EqualTo(0));
		Assert.That(CliTestHost.Run(new[] { "key", "--pid", "1234", "--target", "0002" }, services).ExitCode, Is.EqualTo(0));

		Assert.That(session.Session.Commands.OfType<ClickCommandRequest>().Single().MouseButton, Is.EqualTo("right"));
		Assert.That(session.Session.Commands.OfType<TypeTextCommandRequest>().Single().Text, Is.EqualTo("from-default"));
		Assert.That(session.Session.Commands.OfType<TypeTextCommandRequest>().Single().ClearFirst, Is.True);
		Assert.That(session.Session.Commands.OfType<KeyPressCommandRequest>().Single().Keys, Is.EqualTo("Enter"));
		Assert.That(session.Session.Commands.OfType<KeyPressCommandRequest>().Single().DelayMs, Is.EqualTo(15));
		Assert.That(session.Session.Commands.OfType<KeyPressCommandRequest>().Single().EnsureForeground, Is.False);
	}

	[Test]
	public void FocusTypeKeySetRaiseAndInvokeCreateExpectedPayloads()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		Assert.That(CliTestHost.Run(new[] { "focus", "--pid", "1234", "--target", "0002" }, services).ExitCode, Is.EqualTo(0));
		Assert.That(CliTestHost.Run(new[] { "type", "--pid", "1234", "--target", "0002", "--text", "hello", "--clear-first" }, services).ExitCode, Is.EqualTo(0));
		Assert.That(CliTestHost.Run(new[] { "key", "--pid", "1234", "--target", "0002", "--keys", "Ctrl+A", "--delay-ms", "75" }, services).ExitCode, Is.EqualTo(0));
		Assert.That(CliTestHost.Run(new[] { "set", "--pid", "1234", "--target", "0002", "--property", "IsEnabled", "--value", "true" }, services).ExitCode, Is.EqualTo(0));
		Assert.That(CliTestHost.Run(new[] { "raise", "--pid", "1234", "--target", "0002", "--event", "Click" }, services).ExitCode, Is.EqualTo(0));
		Assert.That(CliTestHost.Run(new[] { "invoke", "--pid", "1234", "--target", "0002", "--operation", "Focus" }, services).ExitCode, Is.EqualTo(0));

		Assert.That(session.Session.Commands.OfType<FocusCommandRequest>().Single().TargetId, Is.EqualTo("button-0002"));
		Assert.That(session.Session.Commands.OfType<TypeTextCommandRequest>().Single().ClearFirst, Is.True);
		Assert.That(session.Session.Commands.OfType<KeyPressCommandRequest>().Single().DelayMs, Is.EqualTo(75));
		Assert.That(session.Session.Commands.OfType<KeyPressCommandRequest>().Single().EnsureForeground, Is.True);
		Assert.That(session.Session.Commands.OfType<SetPropertyCommandRequest>().Single().PropertyValue, Is.EqualTo(true));
		Assert.That(session.Session.Commands.OfType<KnownRoutedEventCommandRequest>().Single(command => command.EventName == "Click").TargetId, Is.EqualTo("button-0002"));
		Assert.That(session.Session.Commands.OfType<KnownOperationCommandRequest>().Single().Operation, Is.EqualTo("Focus"));
	}

	[Test]
	public void KeyForegroundOptionOverridesDefault()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "key", "--pid", "1234", "--keys", "Enter", "--foreground", "false" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(session.Session.Commands.OfType<KeyPressCommandRequest>().Single().EnsureForeground, Is.False);
	}

	[Test]
	public void TypeAndKeyCanTargetForegroundWithoutElementSelector()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var typed = CliTestHost.Run(new[] { "type", "--pid", "1234", "--text", "hello" }, services);
		var keyed = CliTestHost.Run(new[] { "key", "--pid", "1234", "--keys", "Enter" }, services);

		Assert.That(typed.ExitCode, Is.EqualTo(0));
		Assert.That(keyed.ExitCode, Is.EqualTo(0));
		Assert.That(session.Session.Commands.OfType<TypeTextCommandRequest>().Single().TargetId, Is.Null);
		Assert.That(session.Session.Commands.OfType<KeyPressCommandRequest>().Single().TargetId, Is.Null);
	}

	[Test]
	public void TypeRequiresTextAndKeyRejectsUnknownName()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var missingText = CliTestHost.Run(new[] { "type", "--pid", "1234", "--target", "0002" }, services);
		var badKey = CliTestHost.Run(new[] { "key", "--pid", "1234", "--keys", "NoSuchKey" }, services);

		Assert.That(missingText.ExitCode, Is.EqualTo(1));
		Assert.That(badKey.ExitCode, Is.EqualTo(1));
		Assert.That(badKey.Stdout, Does.Contain("\"code\":\"invalid-arguments\""));
	}

	[Test]
	public void RaiseAndInvokeValidateAllowListsAndArbitraryGate()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var badEvent = CliTestHost.Run(new[] { "raise", "--pid", "1234", "--target", "0002", "--event", "Anything" }, services);
		var deniedInvoke = CliTestHost.Run(new[] { "invoke", "--pid", "1234", "--target", "0002", "--code", "\"PublicMethod\"" }, services);
		var mixedInvoke = CliTestHost.Run(new[] { "invoke", "--pid", "1234", "--target", "0002", "--operation", "Focus", "--code", "\"PublicMethod\"", "--allow-arbitrary-invoke" }, services);
		var badJson = CliTestHost.Run(new[] { "invoke", "--pid", "1234", "--target", "0002", "--code", "{", "--allow-arbitrary-invoke" }, services);

		Assert.That(badEvent.ExitCode, Is.EqualTo(1));
		Assert.That(deniedInvoke.ExitCode, Is.EqualTo(1));
		Assert.That(deniedInvoke.Stdout, Does.Contain("\"code\":\"arbitrary-invoke-denied\""));
		Assert.That(mixedInvoke.ExitCode, Is.EqualTo(1));
		Assert.That(mixedInvoke.Stdout, Does.Contain("either --operation or --code"));
		Assert.That(badJson.ExitCode, Is.EqualTo(1));
	}

	[Test]
	public void ArbitraryInvokeWithFlagSendsInvokeRequest()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "invoke", "--pid", "1234", "--target", "0002", "--code", "\"PublicMethod\"", "--allow-arbitrary-invoke" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		var command = session.Session.Commands.OfType<InvokeCommandRequest>().Single();
		Assert.That(command.AllowUnsafeCode, Is.True);
		Assert.That(command.Code, Is.EqualTo("PublicMethod"));
	}

	[Test]
	public void AfterTargetAndAfterTreeReturnSnapshots()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var afterTarget = CliTestHost.Run(new[] { "focus", "--pid", "1234", "--target", "0002", "--after", "target" }, services);
		var afterTree = CliTestHost.Run(new[] { "focus", "--pid", "1234", "--target", "0002", "--after", "tree" }, services);

		Assert.That(afterTarget.ExitCode, Is.EqualTo(0));
		Assert.That(afterTarget.Stdout, Does.Contain("\"after\""));
		Assert.That(afterTarget.Stdout, Does.Contain("\"node\""));
		Assert.That(afterTree.ExitCode, Is.EqualTo(0));
		Assert.That(afterTree.Stdout, Does.Contain("\"shape\":\"flat\""));
	}

	[Test]
	public void SetAfterTargetRequestsSetPropertyAndPayloadErrorsKeepDetails()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var afterSet = CliTestHost.Run(new[] { "set", "--pid", "1234", "--target", "0002", "--property", "CustomProp", "--value", "1", "--after", "target" }, services);
		var afterSetRequestedProperty = session.Session.Commands.OfType<GetVisualTreeCommandRequest>().Last().PropNames;
		session.Session.ActionResponse = DeepFlowTest.Contracts.StandardIpcResponse.FromError("bad target", DeepFlowTest.Contracts.ProtocolConstants.ErrorCodes.UnsupportedTarget);
		var failed = CliTestHost.Run(new[] { "click", "--pid", "1234", "--target", "0002" }, services);

		Assert.That(afterSet.ExitCode, Is.EqualTo(0));
		Assert.That(afterSetRequestedProperty, Does.Contain("CustomProp"));
		Assert.That(failed.ExitCode, Is.EqualTo(3));
		Assert.That(failed.Stdout, Does.Contain("\"details\""));
		Assert.That(failed.Stdout, Does.Contain("bad target"));
	}

	[Test]
	public void TypeCanSeparateTypedValueFromTextSelectorAndIndexRejectsNegative()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var typed = CliTestHost.Run(new[] { "type", "--pid", "1234", "--selector-text", "Submit", "--value", "hello" }, services);
		var badIndex = CliTestHost.Run(new[] { "click", "--pid", "1234", "--automation-id", "SubmitButton", "--index", "-1" }, services);

		Assert.That(typed.ExitCode, Is.EqualTo(0));
		Assert.That(session.Session.Commands.OfType<TypeTextCommandRequest>().Last().Text, Is.EqualTo("hello"));
		Assert.That(badIndex.ExitCode, Is.EqualTo(1));
		Assert.That(badIndex.Stdout, Does.Contain("\"code\":\"invalid-arguments\""));
	}

	[Test]
	public void StrictModeDeniesBeforeSessionOpen()
	{
		var previous = System.Environment.GetEnvironmentVariable("DEEPFLOWTEST_CLI_STRICT_ACTIONS");
		var session = new FakeAppSessionService();
		try
		{
			System.Environment.SetEnvironmentVariable("DEEPFLOWTEST_CLI_STRICT_ACTIONS", "1");
			var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

			var result = CliTestHost.Run(new[] { "click", "--pid", "1234", "--target", "0002" }, services);

			Assert.That(result.ExitCode, Is.EqualTo(1));
			Assert.That(result.Stdout, Does.Contain("\"code\":\"action-denied\""));
			Assert.That(session.LastTarget, Is.Null);
		}
		finally
		{
			System.Environment.SetEnvironmentVariable("DEEPFLOWTEST_CLI_STRICT_ACTIONS", previous);
		}
	}
}
