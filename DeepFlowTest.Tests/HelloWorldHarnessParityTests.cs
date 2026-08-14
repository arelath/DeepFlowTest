namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using HelloWorld;
using NUnit.Framework;
using static DeepFlowTest.Tests.TestIpcHost;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class HelloWorldHarnessParityTests
{
	private static readonly IReadOnlyList<string> CommonProps =
	[
		KnownProperties.Name,
		KnownProperties.AutomationName,
		KnownProperties.AutomationId,
		KnownProperties.Text,
		KnownProperties.Content,
		KnownProperties.Header,
		KnownProperties.IsChecked,
		KnownProperties.IsEnabled,
		KnownProperties.IsExpanded,
		KnownProperties.IsOpen,
		KnownProperties.IsVisible,
		KnownProperties.Visibility,
	];

	[Test]
	public void SnapshotIncludesReferenceExampleControls()
	{
		using var harness = ShowHarness();

		var names = new[]
		{
			"EventDisplay",
			"HelloWorldButton",
			"TogglePopupButton",
			"myPopup",
			"myPopupText",
			"ThrowExceptionButton",
			"DelayedRevealButton",
			"DisabledWaitButton",
			"DelayedReadyText",
			"OpenNewWindowButton",
			"OpenMessageBoxButton",
			"OpenFileDialogButton",
			"MainCheckbox",
			"TextBox1",
			"ListBoxItem1",
			"ListBoxItem2",
			"ListBoxItem3",
			"MenuItemOne",
			"MenuItemTwo",
			"ExpanderControl",
			"ScrollViewer",
			"SecondTextBlock",
			"HostedWinFormsContainer",
		};

		foreach (var name in names)
			Assert.That(FindByName(name), Is.Not.Null, name);

		Assert.That(FindByAutomationId("HelloWorldInput"), Is.Not.Null);
		Assert.That(FindByAutomationId("HelloWorldWindow"), Is.Not.Null);
	}

	[Test]
	public void ControlsCanBeDrivenLikeReferenceExampleApp()
	{
		using var harness = ShowHarness();

		AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("HelloWorldButton") }));
		AssertEventText("HelloWorldButton_Click event triggered.");

		AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("HelloWorldButton"), ClickCount = 2 }));
		AssertEventText("HelloWorldButton_DoubleClick event triggered.");

		AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("HelloWorldButton"), MouseButton = MouseButtonKind.Right }));
		AssertEventText("HelloWorldButton_RightClick event triggered.");

		AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("HelloWorldButton"), MouseButton = MouseButtonKind.Middle }));
		AssertEventText("HelloWorldButton_MiddleClick event triggered.");

		AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("FileContextMenuItem") }));
		AssertEventText("HelloWorldContextMenuFile_Click event triggered.");

		AssertOk(Send(new KnownOperationCommandRequest { TargetId = TargetIdByName("MainCheckbox"), Operation = "Uncheck" }));
		AssertEventText("MainCheckbox_Unchecked event triggered.");
		AssertOk(Send(new KnownOperationCommandRequest { TargetId = TargetIdByName("MainCheckbox"), Operation = "Check" }));
		AssertEventText("MainCheckbox_Checked event triggered.");

		AssertOk(Send(new SetPropertyCommandRequest { TargetId = TargetIdByName("TextBox1"), PropertyName = KnownProperties.Text, PropertyValue = string.Empty }));
		AssertOk(Send(new FocusCommandRequest { TargetId = TargetIdByName("TextBox1") }));
		AssertOk(Send(new TypeTextCommandRequest { TargetId = TargetIdByName("TextBox1"), Text = "Hello World!", ClearFirst = true }));
		Assert.That(Property<string>(FindByName("TextBox1"), KnownProperties.Text), Is.EqualTo("Hello World!"));
		AssertOk(Send(new SetPropertyCommandRequest { TargetId = TargetIdByName("TextBox1"), PropertyName = "SelectionStart", PropertyValue = 0 }));
		AssertOk(Send(new SetPropertyCommandRequest { TargetId = TargetIdByName("TextBox1"), PropertyName = "SelectionLength", PropertyValue = 5 }));
		AssertEventText("TextBox1_SelectionChanged event triggered.");

		AssertOk(Send(new KnownOperationCommandRequest { TargetId = TargetIdByName("ListBoxItem2"), Operation = "Select" }));
		AssertEventText("ListBoxItem2 selected event triggered.");

		AssertOk(Send(new KnownOperationCommandRequest { TargetId = TargetIdByName("ExpanderControl"), Operation = "Expand" }));
		AssertEventText("ExpanderControl_Expanded event triggered.");
		AssertOk(Send(new KnownOperationCommandRequest { TargetId = TargetIdByName("ExpanderControl"), Operation = "Collapse" }));
		AssertEventText("ExpanderControl_Collapsed event triggered.");

		AssertOk(Send(new KnownOperationCommandRequest { TargetId = TargetIdByName("MenuItemOne"), Operation = "Check" }));
		Assert.That(Property<bool>(FindByName("MenuItemOne"), KnownProperties.IsChecked), Is.True);

		AssertOk(Send(new KnownOperationCommandRequest { TargetId = TargetIdByName("TogglePopupButton"), Operation = "Check" }));
		Assert.That(Property<bool>(FindByName("myPopup"), KnownProperties.IsOpen), Is.True);
		Assert.That(Property<string>(FindByName("myPopupText"), KnownProperties.Text), Is.EqualTo("Popup Text"));

		AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("DelayedRevealButton") }));
		Assert.That(WaitUntil(() => Property<string>(FindByName("DelayedReadyText"), KnownProperties.Visibility) == "Visible"), Is.True);
		AssertEventText("DelayedReadyText revealed.");

		AssertSecondaryWindowCanOpenAndClose();
		AssertHostedWinFormsControlsCanBeDriven();
	}

	[Test]
	public void DialogLauncherButtonsReportMessageBoxAndFileDialogResults()
	{
		using var harness = ShowHarness();

		MainWindow.ShowMessageBoxForTests = _ => MessageBoxResult.Yes;
		AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("OpenMessageBoxButton"), TimeoutMs = 10_000 }));
		AssertEventText("Chose Yes.");

		var selectedFile = Path.Combine(Path.GetTempPath(), "deepflow-selected-file.txt");
		MainWindow.ShowOpenFileDialogForTests = _ => selectedFile;
		AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("OpenFileDialogButton"), TimeoutMs = 10_000 }));
		AssertEventText("Opened file: deepflow-selected-file.txt");

		MainWindow.ShowOpenFileDialogForTests = _ => null;
		AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("OpenFileDialogButton"), TimeoutMs = 10_000 }));
		AssertEventText("Open file dialog canceled.");
	}

	private static HarnessScope ShowHarness()
	{
		var window = new MainWindow
		{
			ShowInTaskbar = false,
			WindowStartupLocation = WindowStartupLocation.Manual,
			Left = -20000,
			Top = -20000,
		};

		window.Show();
		DoEvents();
		return new HarnessScope(window);
	}

	private static void AssertSecondaryWindowCanOpenAndClose()
	{
		var observed = false;
		var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
		timer.Tick += (_, _) =>
		{
			var otherWindow = FindOpenOtherWindow();
			if (otherWindow is null)
				return;

			if (otherWindow.FindName("OtherWindowCheckBox") is CheckBox checkBox)
				checkBox.IsChecked = true;

			observed = true;
			timer.Stop();
			otherWindow.Close();
		};

		timer.Start();
		try
		{
			AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("OpenNewWindowButton"), TimeoutMs = 5_000 }));
		}
		finally
		{
			timer.Stop();
		}

		Assert.That(observed, Is.True);
	}

	private static void AssertHostedWinFormsControlsCanBeDriven()
	{
		AssertOk(Send(new SetPropertyCommandRequest { TargetId = TargetIdByName("HostedWinFormsContainer"), PropertyName = KnownProperties.Visibility, PropertyValue = "Visible" }));
		DoEvents();

		Assert.That(FindByName("WinFormsHostIsland").TypeName, Is.EqualTo("WindowsFormsHost"));
		Assert.That(FindByName("HostedWinFormsPanel").TypeName, Is.EqualTo("Panel"));

		AssertOk(Send(new ClickCommandRequest { TargetId = TargetIdByName("HostedWinFormsButton") }));
		AssertEventText("HostedWinFormsButton_Click event triggered.");

		AssertOk(Send(new TypeTextCommandRequest { TargetId = TargetIdByName("HostedWinFormsTextBox"), Text = "hosted text", ClearFirst = true }));
		Assert.That(Property<string>(FindByName("HostedWinFormsTextBox"), KnownProperties.Text), Is.EqualTo("hosted text"));
		AssertEventText("HostedWinFormsTextBox_TextChanged: hosted text");

		AssertOk(Send(new KnownOperationCommandRequest { TargetId = TargetIdByName("HostedWinFormsCheckBox"), Operation = "Check" }));
		AssertEventText("HostedWinFormsCheckBox_CheckedChanged: checked");
	}

	private static OtherWindow? FindOpenOtherWindow()
	{
		return PresentationSource.CurrentSources
			.OfType<HwndSource>()
			.Select(static source => source.RootVisual)
			.OfType<OtherWindow>()
			.FirstOrDefault(static window => window.IsVisible);
	}

	private static FindElementMatchResponse FindByName(string name)
	{
		var rawResponse = Send(new FindElementCommandRequest
		{
			Selector = new ElementSelectorDto { Name = name },
			PropNames = CommonProps,
			MaxMatches = 1,
			TimeoutMs = 10_000,
		});
		Assert.That(rawResponse, Is.TypeOf<FindElementCommandResponse>(), DescribeUnexpectedResponse(rawResponse));
		var response = (FindElementCommandResponse)rawResponse!;

		Assert.That(response.MatchCount, Is.EqualTo(1), $"{name}. {DescribeAvailableNodes()}");
		return response.Matches[0];
	}

	private static FindElementMatchResponse FindByAutomationId(string automationId)
	{
		var rawResponse = Send(new FindElementCommandRequest
		{
			Selector = new ElementSelectorDto { AutomationId = automationId },
			PropNames = CommonProps,
			MaxMatches = 1,
			TimeoutMs = 10_000,
		});
		Assert.That(rawResponse, Is.TypeOf<FindElementCommandResponse>(), DescribeUnexpectedResponse(rawResponse));
		var response = (FindElementCommandResponse)rawResponse!;

		Assert.That(response.MatchCount, Is.EqualTo(1), $"{automationId}. {DescribeAvailableNodes()}");
		return response.Matches[0];
	}

	private static string DescribeAvailableNodes()
	{
		var rawResponse = Send(new GetVisualTreeCommandRequest
		{
			AsSnapshot = true,
			PropNames = [KnownProperties.Name, KnownProperties.AutomationId, KnownProperties.Text, KnownProperties.Content, KnownProperties.Title],
			MaxNodeCount = 80,
			TimeoutMs = 10_000,
		});
		if (rawResponse is not VisualTreeSnapshot snapshot)
			return DescribeUnexpectedResponse(rawResponse);

		var summaries = snapshot.Nodes
			.Take(20)
			.Select(static node =>
			{
				var name = node.Properties.TryGetValue(KnownProperties.Name, out var nameValue) ? nameValue : null;
				var automationId = node.Properties.TryGetValue(KnownProperties.AutomationId, out var automationIdValue) ? automationIdValue : null;
				var text = node.Properties.TryGetValue(KnownProperties.Text, out var textValue) ? textValue : null;
				var content = node.Properties.TryGetValue(KnownProperties.Content, out var contentValue) ? contentValue : null;
				var title = node.Properties.TryGetValue("Title", out var titleValue) ? titleValue : null;
				return $"{node.TypeName}: Name={name}; AutomationId={automationId}; Text={text}; Content={content}; Title={title}";
			});

		return string.Join(" | ", summaries);
	}

	private static string DescribeUnexpectedResponse(object? response)
	{
		if (response is StandardIpcResponse standard)
			return $"{standard.ErrorCode}: {standard.Error}";

		return response?.GetType().FullName ?? "null response";
	}

	private static string TargetIdByName(string name) => FindByName(name).TargetId;

	private static T? Property<T>(FindElementMatchResponse match, string propertyName)
	{
		Assert.That(match.Properties.TryGetValue(propertyName, out var value), Is.True, propertyName);
		Assert.That(value, Is.Not.TypeOf<DeepFlowTest.Utility.WpfUtility.Tree.PropertyExtractionError>(), propertyName);
		if (value is null)
			return default;
		if (value is T typed)
			return typed;
		return (T)Convert.ChangeType(value, typeof(T));
	}

	private static void AssertEventText(string expected)
	{
		Assert.That(Property<string>(FindByName("EventDisplay"), KnownProperties.Text), Is.EqualTo(expected));
	}

	private static void AssertOk(object? response)
	{
		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		var standard = (StandardIpcResponse)response!;
		Assert.That(standard.Success, Is.True, standard.Error);
		Assert.That(standard.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
	}

	private static object? Send(object request)
	{
		try
		{
			return CaptureResponse(request, logPrefix: "deepflowtest-helloworld");
		}
		catch (Exception ex)
		{
			Assert.Fail($"Command failed: {ex}");
			return null;
		}
	}

	private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5_000)
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

	private sealed class HarnessScope : IDisposable
	{
		private readonly Window window;

		public HarnessScope(Window window)
		{
			this.window = window;
		}

		public void Dispose()
		{
			MainWindow.ShowMessageBoxForTests = null;
			MainWindow.ShowOpenFileDialogForTests = null;

			foreach (Window openWindow in PresentationSource.CurrentSources
				.OfType<HwndSource>()
				.Select(static source => source.RootVisual)
				.OfType<Window>()
				.Where(openWindow => !ReferenceEquals(openWindow, window))
				.ToArray())
			{
				openWindow.Close();
			}

			window.Close();
			DoEvents();
		}
	}
}
