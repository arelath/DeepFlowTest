namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;
using WpfKey = System.Windows.Input.Key;

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

	private sealed class FakeSession : IAppDriverCommandSession
	{
		private readonly Queue<object> responses;

		public FakeSession(params object[] responses)
		{
			this.responses = new Queue<object>(responses);
		}

		public List<IpcCommand> SentCommands { get; } = new();

		public TResponse Send<TResponse>(IpcCommand command)
		{
			SentCommands.Add(command);
			return (TResponse)responses.Dequeue();
		}
	}

	private sealed class FakeTargetProcess : ITargetProcess
	{
		public int Id => 123;

		public string ProcessName => "target";

		public bool HasExited { get; private set; }

		public void Kill()
		{
			HasExited = true;
		}

		public void Dispose()
		{
		}
	}
}
