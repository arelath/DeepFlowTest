namespace DeepFlowTest.Tests;

using System;
using System.Linq.Expressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class TargetActionCommandTests
{
	[Test]
	public void ClickAndKnownRoutedEventChangeHarnessState()
	{
		var clickCount = 0;
		var rightClickCount = 0;
		var button = new Button { Name = "actionButton", Content = "Ready" };
		button.Click += (_, _) =>
		{
			clickCount++;
			button.Content = "Clicked";
		};
		button.MouseRightButtonUp += (_, _) => rightClickCount++;
		var window = CreateWindow("Click actions", button);

		try
		{
			window.Show();
			var targetId = FindTargetId("actionButton");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId }));
			Assert.That(clickCount, Is.EqualTo(1));
			Assert.That(button.Content, Is.EqualTo("Clicked"));

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId, ClickCount = 2 }));
			Assert.That(clickCount, Is.EqualTo(3));

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId, MouseButton = "right" }));
			Assert.That(rightClickCount, Is.EqualTo(1));

			AssertOk(CaptureResponse(new KnownRoutedEventCommandRequest { TargetId = targetId, EventName = "Click" }));
			Assert.That(clickCount, Is.EqualTo(4));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void FocusTypeTextAndKeyPressUpdateTextField()
	{
		var textBox = new TextBox { Name = "inputBox", Width = 120 };
		var window = CreateWindow("Text actions", textBox);

		try
		{
			window.Show();
			var targetId = FindTargetId("inputBox");

			AssertOk(CaptureResponse(new FocusCommandRequest { TargetId = targetId }));
			Assert.That(textBox.IsKeyboardFocusWithin, Is.True);

			AssertOk(CaptureResponse(new TypeTextCommandRequest { TargetId = targetId, Text = "abc", ClearFirst = true }));
			Assert.That(textBox.Text, Is.EqualTo("abc"));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = targetId, Keys = "d" }));
			Assert.That(textBox.Text, Is.EqualTo("abcd"));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = targetId, Keys = "Control+A", DelayMs = 1, EnsureForeground = true }));
			Assert.That(textBox.SelectionLength, Is.EqualTo(textBox.Text.Length));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void SetPropertyAndKnownOperationsUpdateTargets()
	{
		var panel = new StackPanel();
		var textBox = new TextBox { Name = "setBox", Text = "before" };
		var checkBox = new CheckBox { Name = "checkBox", IsChecked = false };
		panel.Children.Add(textBox);
		panel.Children.Add(checkBox);
		var window = CreateWindow("Set actions", panel);

		try
		{
			window.Show();
			var textBoxId = FindTargetId("setBox");
			var checkBoxId = FindTargetId("checkBox");

			AssertOk(CaptureResponse(new SetPropertyCommandRequest { TargetId = textBoxId, PropertyName = "Text", PropertyValue = "after" }));
			Assert.That(textBox.Text, Is.EqualTo("after"));

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = checkBoxId, Operation = "Check" }));
			Assert.That(checkBox.IsChecked, Is.True);

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = checkBoxId, Operation = "Uncheck" }));
			Assert.That(checkBox.IsChecked, Is.False);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void RaiseEventUsesAllowListedRoutedEvents()
	{
		var checkedCount = 0;
		var checkBox = new CheckBox { Name = "raiseCheckBox" };
		checkBox.Checked += (_, _) => checkedCount++;
		var window = CreateWindow("Raise action", checkBox);

		try
		{
			window.Show();
			var targetId = FindTargetId("raiseCheckBox");

			AssertOk(CaptureResponse(new RaiseEventCommandRequest { TargetId = targetId, EventName = "Checked" }));

			Assert.That(checkedCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void InvokeRequiresExplicitOptIn()
	{
		var textBox = new TextBox { Name = "invokeBox" };
		var window = CreateWindow("Invoke action", textBox);

		try
		{
			window.Show();
			var targetId = FindTargetId("invokeBox");

			var blocked = (StandardIpcResponse)CaptureResponse(new InvokeCommandRequest { TargetId = targetId, Code = "Focus" })!;
			Assert.That(blocked.Success, Is.False);
			Assert.That(blocked.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.UnsupportedCommand));

			AssertOk(CaptureResponse(new InvokeCommandRequest { TargetId = targetId, Code = "Focus", AllowUnsafeCode = true }));
			Assert.That(textBox.IsKeyboardFocusWithin, Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ExpressionInvokeSetPropertyAndRaiseEventRunAgainstTarget()
	{
		var clickCount = 0;
		var panel = new StackPanel();
		var textBox = new TextBox { Name = "expressionBox", Text = "before" };
		var button = new Button { Name = "expressionButton", Content = "Ready" };
		button.Click += (_, _) => clickCount++;
		panel.Children.Add(textBox);
		panel.Children.Add(button);
		var window = CreateWindow("Expression actions", panel);

		try
		{
			window.Show();
			var textBoxId = FindTargetId("expressionBox");
			var buttonId = FindTargetId("expressionButton");
			Expression<Func<TextBox, string>> readText = x => x.Text;
			Expression<Func<TextBox, string>> appendText = x => x.Text + "-after";
			Expression<Func<Button, RoutedEventArgs>> clickArgs = x => new RoutedEventArgs(ButtonBase.ClickEvent);

			var invoke = (StandardIpcResponse)CaptureResponse(new InvokeCommandRequest
			{
				TargetId = textBoxId,
				Code = ExpressionPayloadSerializer.Serialize(readText),
				AllowUnsafeCode = true,
			})!;
			Assert.That(invoke.Success, Is.True, invoke.Error);
			Assert.That(invoke.Value, Is.EqualTo("before"));

			AssertOk(CaptureResponse(new SetPropertyCommandRequest
			{
				TargetId = textBoxId,
				PropertyName = "Text",
				PropertyValue = ExpressionPayloadSerializer.Serialize(appendText),
			}));
			Assert.That(textBox.Text, Is.EqualTo("before-after"));

			AssertOk(CaptureResponse(new RaiseEventCommandRequest
			{
				TargetId = buttonId,
				GetRoutedEventArgs = ExpressionPayloadSerializer.Serialize(clickArgs),
			}));
			Assert.That(clickCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void AsyncExpressionInvokeAwaitsTaskResult()
	{
		var textBox = new TextBox { Name = "asyncExpressionBox", Text = "async" };
		var window = CreateWindow("Async expression action", textBox);

		try
		{
			window.Show();
			var targetId = FindTargetId("asyncExpressionBox");
			Expression<Func<TextBox, Task<string>>> readTextAsync = x => Task.FromResult(x.Text + "-result");

			var response = (StandardIpcResponse)CaptureResponse(new InvokeCommandRequest
			{
				TargetId = targetId,
				Code = ExpressionPayloadSerializer.Serialize(readTextAsync),
				AllowUnsafeCode = true,
				TimeoutMs = 1000,
			})!;

			Assert.That(response.Success, Is.True, response.Error);
			Assert.That(response.Value, Is.EqualTo("async-result"));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void UnsupportedActionReturnsStableErrorWithoutCrashingTarget()
	{
		var textBlock = new TextBlock { Name = "readOnlyText", Text = "Read only" };
		var window = CreateWindow("Unsupported action", textBlock);

		try
		{
			window.Show();
			var targetId = FindTargetId("readOnlyText");

			var response = (StandardIpcResponse)CaptureResponse(new ClickCommandRequest { TargetId = targetId })!;

			Assert.That(response.Success, Is.False);
			Assert.That(response.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.UnsupportedTarget));
			Assert.That(textBlock.Text, Is.EqualTo("Read only"));
		}
		finally
		{
			window.Close();
		}
	}

	private static string FindTargetId(string name)
	{
		var response = (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
		{
			Selector = new ElementSelectorDto { Name = name },
			PropNames = new[] { "Name", "Text", "Content", "AutomationProperties.AutomationId" },
			MaxMatches = 1,
		})!;

		Assert.That(response.MatchCount, Is.EqualTo(1), name);
		return response.Matches[0].TargetId;
	}

	private static void AssertOk(object? response)
	{
		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		var standard = (StandardIpcResponse)response!;
		Assert.That(standard.Success, Is.True, standard.Error);
		Assert.That(standard.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
	}

	private static object? CaptureResponse(object request)
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");
		object? response = null;
		var responseCount = 0;
		var command = new NamedPipeServer.Command
		{
			Value = request,
			Respond = value =>
			{
				response = value;
				responseCount++;
			},
			CheckHasResponded = () => responseCount != 0,
			HoldConnectionOpen = () => { },
			TrySend = value =>
			{
				response = value;
				responseCount++;
				return true;
			},
		};
		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = "test-pipe",
			Mode = PayloadStartupModes.OneShotDriver,
			PayloadRoot = AppContext.BaseDirectory,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};

		var dispatcherType = Type.GetType("DeepFlowTest.AppDriverPayload.AppDriverCommandDispatcher, DeepFlowTest", throwOnError: true)!;
		var method = dispatcherType.GetMethod("Process", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
		method.Invoke(null, new object?[] { command, options, null });
		return response;
	}

	private static Window CreateWindow(string title, object content)
	{
		return new Window
		{
			Title = title,
			Content = content,
			Width = 260,
			Height = 180,
			ShowInTaskbar = false,
			WindowStartupLocation = WindowStartupLocation.Manual,
			Left = -20000,
			Top = -20000,
		};
	}
}
