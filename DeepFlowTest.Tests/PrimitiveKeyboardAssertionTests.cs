namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Tests.Fakes;
using NUnit.Framework;
using WpfKey = System.Windows.Input.Key;
using FakeSession = DeepFlowTest.Tests.Fakes.FakeAppDriverCommandSession;

[TestFixture]
public sealed class PrimitiveKeyboardAssertionTests
{
	[Test]
	public void PrimitiveConvertsScalarPropertyValues()
	{
		var element = CreateDriver(new FakeSession(FindMatch("target", "value", ("Count", "7"))))
			.GetElement(ElementSelector.ByName("value"));

		var primitive = Primitive.FromProperty(element, "Count");

		Assert.That(primitive.TargetId, Is.EqualTo("target"));
		Assert.That(primitive.PropertyName, Is.EqualTo("Count"));
		Assert.That(primitive.As<int>(), Is.EqualTo(7));
		Assert.That(primitive.To<int>(), Is.EqualTo(7));
		Assert.That(primitive.S, Is.EqualTo("7"));
		Assert.That(primitive > 6, Is.True);
		Assert.That(primitive == "7", Is.True);
		Assert.That(primitive.ToString(), Is.EqualTo("7"));
	}

	[Test]
	public void PrimitiveSupportsCompatibilityValueStyleOperators()
	{
		Primitive five = 5;
		Primitive two = 2;
		Primitive text = "Hello";
		Primitive truth = true;
		Primitive flags = 0b1010;
		Primitive nullableNumber = (int?)8;
		Primitive modeName = "Second";

		Assert.That(five > two, Is.True);
		Assert.That((five + two).To<int>(), Is.EqualTo(7));
		Assert.That((five - two).To<int>(), Is.EqualTo(3));
		Assert.That((five * two).To<int>(), Is.EqualTo(10));
		Assert.That((five / two).To<int>(), Is.EqualTo(2));
		Assert.That((five % two).To<int>(), Is.EqualTo(1));
		Assert.That((text + " world").S, Is.EqualTo("Hello world"));
		Assert.That((!truth).To<bool>(), Is.False);
		Assert.That((truth & false).To<bool>(), Is.False);
		Assert.That((truth | false).To<bool>(), Is.True);
		Assert.That((flags & 0b0010).To<int>(), Is.EqualTo(2));
		Assert.That((flags ^ 0b1111).To<int>(), Is.EqualTo(5));
		Assert.That((two << 2).To<int>(), Is.EqualTo(8));
		Assert.That((five >> 1).To<int>(), Is.EqualTo(2));
		Assert.That((~two).To<int>(), Is.EqualTo(~2));
		Assert.That((+five).To<int>(), Is.EqualTo(5));
		Assert.That((-five).To<int>(), Is.EqualTo(-5));
		Assert.That((++five).To<int>(), Is.EqualTo(6));
		Assert.That((--five).To<int>(), Is.EqualTo(5));
		Assert.That(nullableNumber.To<int?>(), Is.EqualTo(8));
		Assert.That(modeName.To<SampleMode>(), Is.EqualTo(SampleMode.Second));
		Assert.That(Primitive.Empty.S, Is.Empty);
		Assert.That(Primitive.Empty.To<int?>(), Is.Null);
	}

	[Test]
	public void KeyboardTypingAndModifierSequenceSendCommands()
	{
		var session = new FakeSession(
			FindMatch("input", "inputBox"),
			StandardIpcResponse.Ok(),
			StandardIpcResponse.Ok(),
			StandardIpcResponse.Ok());
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("inputBox"));
		var keyboard = driver.Keyboard;
		keyboard.DelayMs = 12;
		keyboard.EnsureForeground = false;

		keyboard.Type(element, "hello", clearFirst: true);
		keyboard.Press(element, "Enter");
		keyboard.Shortcut(element, "Control", "A");

		var typeCommand = session.SentCommands.OfType<TypeTextCommandRequest>().Single();
		var keyCommands = session.SentCommands.OfType<KeyPressCommandRequest>().ToArray();
		Assert.That(typeCommand.TargetId, Is.EqualTo("input"));
		Assert.That(typeCommand.Text, Is.EqualTo("hello"));
		Assert.That(typeCommand.ClearFirst, Is.True);
		Assert.That(keyCommands.Select(static command => command.Keys), Is.EqualTo(new object?[] { "Enter", "Control+A" }));
		Assert.That(keyCommands.All(command => command.DelayMs == 12), Is.True);
		Assert.That(keyCommands.All(command => command.EnsureForeground == false), Is.True);
	}

	[Test]
	public void KeyboardExposesCompatibilityPhysicalInputOverloads()
	{
		Assert.That(typeof(Keyboard).GetMethod(nameof(Keyboard.Press), new[] { typeof(WpfKey[]) }), Is.Not.Null);
		Assert.That(typeof(Keyboard).GetMethod(nameof(Keyboard.Type), new[] { typeof(string) }), Is.Not.Null);
	}

	[Test]
	public void AssertionFailureIncludesSelectorTargetIdAndLastProperties()
	{
		var element = CreateDriver(new FakeSession(FindMatch("button", "submit", ("Content", "Cancel"))))
			.GetElement(ElementSelector.ByName("submit"));

		var exception = Assert.Throws<AppDriverAssertionException>(() => element.ShouldHaveProperty("Content", "Save"));

		Assert.That(exception!.Message, Does.Contain("TargetId=button"));
		Assert.That(exception.Message, Does.Contain("Selector=Name=submit"));
		Assert.That(exception.Message, Does.Contain("Cancel"));
	}

	private static AppDriver CreateDriver(IAppDriverCommandSession session)
	{
		return AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session);
	}

	private static FindElementCommandResponse FindMatch(string targetId, string name, params (string Key, object? Value)[] properties)
	{
		var match = new FindElementMatchResponse
		{
			TargetId = targetId,
			TypeName = "TextBox",
			Properties = { ["Name"] = name },
		};
		foreach (var property in properties)
			match.Properties[property.Key] = property.Value;

		return new FindElementCommandResponse
		{
			Matches = { match },
			MatchCount = 1,
		};
	}

	private enum SampleMode
	{
		First,
		Second,
	}

}
