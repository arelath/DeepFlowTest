namespace DeepFlowTest.Tests;

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Tests.Fakes;
using NUnit.Framework;
using static DeepFlowTest.Tests.TestIpcHost;
using static DeepFlowTest.Tests.WpfTestHelpers;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class AppDriverInProcessEndToEndTests
{
	[Test]
	public void PublicElementActionsDriveWpfTargetsThroughPayloadDispatcher()
	{
		var clickCount = 0;
		var doubleClickCount = 0;
		var button = new Button { Name = "inProcessButton", Content = "Run", Width = 90, Height = 28 };
		button.Click += (_, _) => clickCount++;
		button.MouseDoubleClick += (_, _) => doubleClickCount++;
		var textBox = new TextBox { Name = "inProcessTextBox", Width = 140 };
		var checkBox = new CheckBox { Name = "inProcessCheckBox", Content = "Check" };
		var expander = new Expander
		{
			Name = "inProcessExpander",
			Header = "Details",
			Content = new TextBlock { Text = "Expanded content" },
		};
		var panel = new StackPanel();
		panel.Children.Add(button);
		panel.Children.Add(textBox);
		panel.Children.Add(checkBox);
		panel.Children.Add(expander);
		var window = CreateWindow("In-process AppDriver end-to-end", panel);

		try
		{
			window.Show();
			DoEvents();
			using var driver = AppDriver.CreateForTests(
				AppConnection.ForAttach(new FakeTargetProcess(), "in-process-payload"),
				new InProcessPayloadSession());

			driver.GetElement(ElementSelector.ByName("inProcessButton"))
				.Click()
				.DoubleClick();
			var inputElement = driver.GetElement(ElementSelector.ByName("inProcessTextBox"));
			inputElement
				.Type("hello", clearFirst: true)
				.SetProperty("Text", "updated");
			driver.Keyboard.DelayMs = 1;
			driver.Keyboard.Shortcut(inputElement, "Control", "A");
			driver.Keyboard.Press(inputElement, "Backspace");
			driver.GetElement(ElementSelector.ByName("inProcessCheckBox"))
				.Check()
				.Uncheck();
			driver.GetElement(ElementSelector.ByName("inProcessExpander"))
				.Expand()
				.Collapse();

			Assert.That(clickCount, Is.EqualTo(1));
			Assert.That(doubleClickCount, Is.EqualTo(1));
			Assert.That(textBox.Text, Is.Empty);
			Assert.That(checkBox.IsChecked, Is.False);
			Assert.That(expander.IsExpanded, Is.False);
		}
		finally
		{
			window.Close();
			DoEvents();
		}
	}

	private static void DoEvents()
	{
		var frame = new DispatcherFrame();
		Dispatcher.CurrentDispatcher.BeginInvoke(
			DispatcherPriority.Background,
			new DispatcherOperationCallback(_ =>
			{
				frame.Continue = false;
				return null;
			}),
			null);
		Dispatcher.PushFrame(frame);
	}

	private sealed class InProcessPayloadSession : IAppDriverCommandSession
	{
		public TResponse Send<TResponse>(IpcCommand command)
		{
			var response = CaptureResponse(command, logPrefix: "deepflowtest-appdriver-e2e");
			Assert.That(response, Is.Not.Null, command.Kind);
			return (TResponse)response!;
		}
	}
}
