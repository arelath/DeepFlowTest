namespace DeepFlowTest.Tests;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Shared;
using DeepFlowTest.Tests.Fakes;
using DeepFlowTest.Utility.WpfUtility.Tree;
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

	[Test]
	[NonParallelizable]
	public void MessageBoxShowReportsPendingModalDialogThroughPayloadDispatcher()
	{
		var caption = $"DeepFlowTest MessageBox hook {Guid.NewGuid():N}";
		var button = new Button { Name = "messageBoxHookButton", Content = "Show message", Width = 120, Height = 28 };
		var window = CreateWindow("MessageBox hook", button);
		button.Click += (_, _) => MessageBox.Show(window, "MessageBox hook content", caption, MessageBoxButton.OK);

		Task? closeDialogTask = null;
		try
		{
			window.Show();
			DoEvents();
			using var nativeDialogFallback = NativeDialogService.OverrideRootWindowsForTests([]);
			using var driver = AppDriver.CreateForTests(
				AppConnection.ForAttach(new FakeTargetProcess(), "in-process-payload"),
				new InProcessPayloadSession());
			var target = driver.GetElement(ElementSelector.ByName("messageBoxHookButton"));

			var responseTask = Task.Run(() => driver.Send<StandardIpcResponse>(new ClickCommandRequest
			{
				TargetId = target.TargetId,
				TimeoutMs = 1_000,
			}));
			closeDialogTask = CloseNativeDialogAfterResponseAsync(caption, responseTask);

			Assert.That(
				WaitUntil(() => responseTask.IsCompleted, 5_000),
				Is.True,
				"Click command did not return while MessageBox.Show was still modal.");
			var response = responseTask.GetAwaiter().GetResult();

			Assert.That(response.Success, Is.True, response.Error);
			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.PendingResult));
		}
		finally
		{
			CloseNativeDialogByCaption(caption, TimeSpan.FromMilliseconds(250));
			try
			{
				closeDialogTask?.Wait(TimeSpan.FromSeconds(5));
			}
			catch (AggregateException)
			{
			}

			window.Close();
			DoEvents();
		}
	}

	private static Task CloseNativeDialogAfterResponseAsync(string caption, Task responseTask) =>
		Task.Run(() =>
		{
			try
			{
				responseTask.Wait(TimeSpan.FromSeconds(2));
			}
			catch (AggregateException)
			{
			}

			CloseNativeDialogByCaption(caption, TimeSpan.FromSeconds(3));
		});

	private static bool CloseNativeDialogByCaption(string caption, TimeSpan timeout)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			var hwnd = NativeMethods.FindWindow("#32770", caption);
			if (hwnd == IntPtr.Zero)
				hwnd = NativeMethods.FindWindow(null, caption);

			if (hwnd != IntPtr.Zero)
			{
				NativeMethods.SendMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
				return true;
			}

			Thread.Sleep(25);
		}

		return false;
	}

	private static bool WaitUntil(Func<bool> condition, int timeoutMs)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds < timeoutMs)
		{
			if (condition())
				return true;

			DoEvents();
			Thread.Sleep(25);
		}

		return condition();
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
