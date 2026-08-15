namespace DeepFlowTest.Cli.Tests;

using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class AutomationActionPipelineTests
{
	[Test]
	public void RegistryCoversEveryAutomationActionAndBuildsTypedCommands()
	{
		Assert.DoesNotThrow(AutomationActionRegistry.ValidateRegistrations);

		AutomationAction[] actions =
		[
			new ClickAction(MouseButtonKind.Right, 2),
			new MouseWheelAction(-120),
			new DragAction(),
			new FocusAction(),
			new TypeTextAction("hello", ClearFirst: true),
			new KeyPressAction("Control+A", 10, EnsureForeground: true),
			new SetPropertyAction("Text", "value"),
			new RoutedEventAction("Checked"),
			new KnownOperationAction("Select"),
			new InvokeCodeAction(42L, AllowUnsafeCode: true),
		];

		var commands = actions.Select(action => AutomationActionRegistry.CreateCommand(action, "source-0001", "destination-0002", 500)).ToArray();
		Assert.That(commands.Select(command => command.GetType()), Is.EqualTo(new[]
		{
			typeof(ClickCommandRequest),
			typeof(MouseWheelCommandRequest),
			typeof(DragAndDropCommandRequest),
			typeof(FocusCommandRequest),
			typeof(TypeTextCommandRequest),
			typeof(KeyPressCommandRequest),
			typeof(SetPropertyCommandRequest),
			typeof(KnownRoutedEventCommandRequest),
			typeof(KnownOperationCommandRequest),
			typeof(InvokeCommandRequest),
		}));
	}

	[Test]
	public void PipelineOwnsPolicyValidationExecutionInvalidationAndPostActionHooks()
	{
		var session = new FakeCliAppSession();
		var calls = new List<string>();
		var pipeline = new AutomationActionPipeline();
		var result = pipeline.Execute(
			session,
			new AutomationExecutionOptions(500, 100, [], ObservationMode.None, UseShortIds: true),
			new AutomationActionRequest(new MouseWheelAction(-240), new DeepFlowTest.Automation.ElementSelector { TargetId = "button-0002" }),
			new AutomationActionPipelineHooks
			{
				DemandPolicy = descriptor => calls.Add($"policy:{descriptor.Name}"),
				InvalidateCache = () => calls.Add("invalidate"),
				Verify = _ => calls.Add("verify"),
				Observe = _ => calls.Add("observe"),
			});

		Assert.That(result.Action, Is.EqualTo("wheel"));
		Assert.That(session.Commands.OfType<MouseWheelCommandRequest>().Single().Delta, Is.EqualTo(-240));
		Assert.That(calls, Is.EqualTo(new[] { "policy:wheel", "invalidate", "verify", "observe" }));
	}

	[TestCaseSource(nameof(InvalidActions))]
	public void SharedRegistryRejectsInvalidActions(AutomationAction action)
	{
		var error = Assert.Throws<AutomationException>(() => AutomationActionRegistry.Validate(action));
		Assert.That(error!.ErrorCode, Is.EqualTo(AutomationErrorCodes.InvalidArguments));
	}

	private static IEnumerable<AutomationAction> InvalidActions()
	{
		yield return new ClickAction(MouseButtonKind.Left, 0);
		yield return new MouseWheelAction(0);
		yield return new KeyPressAction("UnknownSpecialKey", 0, true, ValidateKnownKeys: true);
		yield return new SetPropertyAction(string.Empty, null);
		yield return new RoutedEventAction("Loaded");
		yield return new KnownOperationAction("RunAnything");
	}
}
