namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Shared;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using NUnit.Framework;
using static DeepFlowTest.Tests.TestIpcHost;
using static DeepFlowTest.Tests.WpfTestHelpers;
using Forms = System.Windows.Forms;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class WinFormsSupportTests
{
	[Test]
	public void PureWinFormsRootsAndControlsAppearInSnapshots()
	{
		using var form = CreateForm();
		form.Controls.Add(new Forms.Button { Name = "formsButton", Text = "Click", Width = 90, Height = 28 });

		try
		{
			form.Show();
			Forms.Application.DoEvents();

			var snapshot = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = ["Name", "Text"],
				MaxNodeCount = 200,
			})!;

			Assert.That(snapshot.TargetFrameworkFamily, Is.EqualTo("winforms").Or.EqualTo("mixed"));
			Assert.That(snapshot.Nodes.Any(static node => node.TypeName == "Form"), Is.True);
			Assert.That(snapshot.Nodes.Any(node => node.Properties.TryGetValue("Name", out var value) && Equals(value, "formsButton")), Is.True);
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void SecondaryWinFormsFormsAppearInSnapshots()
	{
		using var mainForm = CreateForm();
		using var secondaryForm = CreateForm();
		secondaryForm.Name = "secondaryFormsRoot";
		secondaryForm.Text = "Secondary WinForms support";
		mainForm.Controls.Add(new Forms.Label { Name = "mainFormsLabel", Text = "Main", Width = 80 });
		secondaryForm.Controls.Add(new Forms.Label { Name = "secondaryFormsLabel", Text = "Secondary", Width = 120 });

		try
		{
			mainForm.Show();
			secondaryForm.Show();
			Forms.Application.DoEvents();

			var snapshot = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = ["Name", "Text"],
				MaxNodeCount = 300,
			})!;

			Assert.That(snapshot.Nodes.Count(static node => node.TypeName == "Form"), Is.GreaterThanOrEqualTo(2));
			Assert.That(snapshot.Nodes.Any(node => node.Properties.TryGetValue("Name", out var value) && Equals(value, "mainFormsLabel")), Is.True);
			Assert.That(snapshot.Nodes.Any(node => node.Properties.TryGetValue("Name", out var value) && Equals(value, "secondaryFormsLabel")), Is.True);
			Assert.That(snapshot.TargetFrameworkFamily, Is.EqualTo("winforms").Or.EqualTo("mixed"));
		}
		finally
		{
			secondaryForm.Close();
			mainForm.Close();
		}
	}

	[Test]
	public void FindElementSelectorContinuesFromWpfRootsIntoWinFormsRoots()
	{
		var window = CreateWindow("Mixed selector WPF root", new Button { Name = "wpfOnlyButton", Content = "WPF only" });
		using var form = CreateForm();
		form.Controls.Add(new Forms.Button { Name = "formsMixedSelectorButton", Text = "WinForms target", Width = 120, Height = 28 });

		try
		{
			window.Show();
			form.Show();
			Forms.Application.DoEvents();

			var response = (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
			{
				Selector = new ElementSelectorDto { Name = "formsMixedSelectorButton" },
				PropNames = ["Name", "Text", "Content"],
				MaxMatches = 1,
			})!;

			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(response.MatchCount, Is.EqualTo(1));
			Assert.That(response.Matches[0].TypeName, Is.EqualTo("Button"));
			Assert.That(response.Matches[0].FrameworkTypeName, Does.StartWith("System.Windows.Forms."));
			Assert.That(response.Matches[0].Properties["Name"], Is.EqualTo("formsMixedSelectorButton"));
		}
		finally
		{
			form.Close();
			window.Close();
		}
	}

	[Test]
	public void ElementExpressionMatcherContinuesFromWpfRootsIntoWinFormsRoots()
	{
		var window = CreateWindow("Mixed expression WPF root", new TextBlock { Name = "wpfOnlyText", Text = "WPF only" });
		using var form = CreateForm();
		form.Controls.Add(new Forms.Button { Name = "formsMixedExpressionButton", Text = "Expression target", Width = 120, Height = 28 });

		try
		{
			window.Show();
			form.Show();
			Forms.Application.DoEvents();
			Expression<Func<DeepFlowTest.Element, bool?>> matcher = element =>
				element.TypeName == "Button"
				&& element["Name"] == "formsMixedExpressionButton"
				&& element["Text"] == "Expression target";

			var response = (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
			{
				MatcherCode = Eval.SerializeCode(matcher),
				MatcherHash = ExpressionPayloadSerializer.Serialize(matcher).ExpressionHash,
				PropNames = ["Name", "Text"],
				MaxMatches = 1,
			})!;

			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(response.MatchCount, Is.EqualTo(1));
			Assert.That(response.Matches[0].FrameworkTypeName, Does.StartWith("System.Windows.Forms."));
			Assert.That(response.Matches[0].Properties["Name"], Is.EqualTo("formsMixedExpressionButton"));
		}
		finally
		{
			form.Close();
			window.Close();
		}
	}

	[Test]
	public void RootMatcherContinuesFromWpfRootsIntoWinFormsRoots()
	{
		var window = CreateWindow("Mixed root matcher WPF root", new Button { Name = "wpfOnlyRootMatcherButton", Content = "WPF only" });
		using var form = CreateForm();
		form.Controls.Add(new Forms.Button { Name = "formsMixedRootMatcherButton", Text = "Root matcher target", Width = 140, Height = 28 });

		try
		{
			window.Show();
			form.Show();
			Forms.Application.DoEvents();
			Expression<Func<DeepFlowTest.Element, bool?>> rootMatcher = element =>
				element.TypeName == "Form" && element["Name"] == "formsRoot";
			Expression<Func<DeepFlowTest.Element, bool?>> matcher = element =>
				element.TypeName == "Button" && element["Name"] == "formsMixedRootMatcherButton";

			var response = (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
			{
				RootMatcherCode = Eval.SerializeCode(rootMatcher),
				RootMatcherHash = ExpressionPayloadSerializer.Serialize(rootMatcher).ExpressionHash,
				IncludeRoot = false,
				MatcherCode = Eval.SerializeCode(matcher),
				MatcherHash = ExpressionPayloadSerializer.Serialize(matcher).ExpressionHash,
				PropNames = ["Name", "Text"],
				MaxMatches = 1,
			})!;

			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(response.MatchCount, Is.EqualTo(1));
			Assert.That(response.Matches[0].FrameworkTypeName, Does.StartWith("System.Windows.Forms."));
			Assert.That(response.Matches[0].Properties["Name"], Is.EqualTo("formsMixedRootMatcherButton"));
		}
		finally
		{
			form.Close();
			window.Close();
		}
	}

	[Test]
	public void WinFormsClickAndTextInputWorkThroughTargetActions()
	{
		using var form = CreateForm();
		var clickCount = 0;
		var button = new Forms.Button { Name = "formsActionButton", Text = "Click", Width = 90, Height = 28 };
		button.Click += (_, _) => clickCount++;
		var textBox = new Forms.TextBox { Name = "formsTextBox", Top = 40, Width = 120 };
		form.Controls.Add(button);
		form.Controls.Add(textBox);

		try
		{
			form.Show();
			Forms.Application.DoEvents();

			var buttonId = FindTargetId("formsActionButton");
			var textBoxId = FindTargetId("formsTextBox");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = buttonId }));
			AssertOk(CaptureResponse(new TypeTextCommandRequest { TargetId = textBoxId, Text = "hello", ClearFirst = true }));

			Assert.That(clickCount, Is.EqualTo(1));
			Assert.That(textBox.Text, Is.EqualTo("hello"));
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void WinFormsKnownOperationsSupportSelectionToggleAndComboExpansion()
	{
		using var form = CreateForm();
		var textBox = new Forms.TextBox { Name = "formsSelectBox", Top = 8, Width = 120 };
		var checkBox = new Forms.CheckBox { Name = "formsKnownCheckBox", Text = "Check", Top = 40, Width = 120 };
		var comboBox = new Forms.ComboBox { Name = "formsKnownComboBox", Top = 72, Width = 120 };
		comboBox.Items.AddRange(new object[] { "One", "Two" });
		form.Controls.Add(textBox);
		form.Controls.Add(checkBox);
		form.Controls.Add(comboBox);

		try
		{
			form.Show();
			Forms.Application.DoEvents();

			var textBoxId = FindTargetId("formsSelectBox");
			var checkBoxId = FindTargetId("formsKnownCheckBox");
			var comboBoxId = FindTargetId("formsKnownComboBox");

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = textBoxId, Operation = "Select" }));
			Assert.That(form.ActiveControl, Is.SameAs(textBox));

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = checkBoxId, Operation = "Check" }));
			Assert.That(checkBox.Checked, Is.True);

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = checkBoxId, Operation = "Uncheck" }));
			Assert.That(checkBox.Checked, Is.False);

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = comboBoxId, Operation = "Expand" }));
			Assert.That(comboBox.DroppedDown, Is.True);

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = comboBoxId, Operation = "Collapse" }));
			Assert.That(comboBox.DroppedDown, Is.False);
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void WinFormsKeyPressSupportsSelectionEditingAndTabNavigation()
	{
		using var form = CreateForm();
		var first = new Forms.TextBox { Name = "formsFirstTextBox", Text = "abcdef", Width = 120, TabIndex = 0 };
		var second = new Forms.TextBox { Name = "formsSecondTextBox", Top = 36, Width = 120, TabIndex = 1 };
		form.Controls.Add(first);
		form.Controls.Add(second);

		try
		{
			form.Show();
			Forms.Application.DoEvents();
			first.Focus();

			var firstId = FindTargetId("formsFirstTextBox");
			var secondId = FindTargetId("formsSecondTextBox");

			second.Focus();
			AssertOk(CaptureResponse(new FocusCommandRequest { TargetId = firstId }));
			Assert.That(form.ActiveControl, Is.SameAs(first));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = firstId, Keys = "Control+A", DelayMs = 1, EnsureForeground = false }));
			Assert.That(first.SelectionLength, Is.EqualTo(6));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = firstId, Keys = "Backspace", DelayMs = 1, EnsureForeground = false }));
			Assert.That(first.Text, Is.Empty);
			Assert.That(first.SelectionStart, Is.EqualTo(0));

			first.Text = "abc";
			first.SelectionStart = 1;
			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = firstId, Keys = "Delete", DelayMs = 1, EnsureForeground = false }));
			Assert.That(first.Text, Is.EqualTo("ac"));
			Assert.That(first.SelectionStart, Is.EqualTo(1));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = firstId, Keys = "Tab", DelayMs = 1, EnsureForeground = false }));
			Assert.That(form.ActiveControl, Is.SameAs(second));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = secondId, Keys = "Shift+Tab", DelayMs = 1, EnsureForeground = false }));
			Assert.That(form.ActiveControl, Is.SameAs(first));
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void DisabledWinFormsButtonClickDoesNotRaiseClick()
	{
		using var form = CreateForm();
		var clickCount = 0;
		var button = new Forms.Button { Name = "disabledFormsButton", Text = "Disabled", Enabled = false, Width = 90, Height = 28 };
		button.Click += (_, _) => clickCount++;
		form.Controls.Add(button);

		try
		{
			form.Show();
			Forms.Application.DoEvents();
			var buttonId = FindTargetId("disabledFormsButton");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = buttonId }));

			Assert.That(clickCount, Is.EqualTo(0));
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void NativeWindowScreenshotAndClickSmoke()
	{
		using var form = CreateForm();
		var clickCount = 0;
		var button = new Forms.Button { Name = "nativeButton", Text = "Native", Width = 90, Height = 28 };
		button.Click += (_, _) => clickCount++;
		form.Controls.Add(button);

		try
		{
			form.Show();
			Forms.Application.DoEvents();

			var snapshot = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = ["Name", "Text"],
				MaxNodeCount = 300,
			})!;
			var hwndNode = snapshot.Nodes.FirstOrDefault(node => node.TypeName == "HWND" && node.Hwnd == button.Handle.ToInt64());
			Assert.That(hwndNode, Is.Not.Null);

			var screenshot = (ScreenshotCommandResponse)CaptureResponse(new ScreenshotCommandRequest { TargetId = hwndNode!.TargetId, Format = ImageFormat.Png })!;
			Assert.That(screenshot.ByteCount, Is.GreaterThan(0));

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = hwndNode.TargetId }));
			for (var i = 0; i < 5 && clickCount == 0; i++)
			{
				Forms.Application.DoEvents();
				Thread.Sleep(10);
			}

			Assert.That(clickCount, Is.EqualTo(1));
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void NativeWindowFileNamePropertySetsWindowText()
	{
		using var form = CreateForm();
		var textBox = new Forms.TextBox { Name = "nativeFileNameBox", Width = 160 };
		form.Controls.Add(textBox);

		try
		{
			form.Show();
			Forms.Application.DoEvents();

			var snapshot = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = ["Name", "Text"],
				MaxNodeCount = 300,
			})!;
			var hwndNode = snapshot.Nodes.FirstOrDefault(node => node.TypeName == "HWND" && node.Hwnd == textBox.Handle.ToInt64());
			Assert.That(hwndNode, Is.Not.Null);

			AssertOk(CaptureResponse(new SetPropertyCommandRequest
			{
				TargetId = hwndNode!.TargetId,
				PropertyName = "FileName",
				PropertyValue = @"C:\temp\selected.txt",
			}));
			Forms.Application.DoEvents();

			Assert.That(textBox.Text, Is.EqualTo(@"C:\temp\selected.txt"));
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void NativeDragAndDropPostsMouseMessagesToTargetHwndWhenCovered()
	{
		using var form = new RecordingDragForm
		{
			Name = "dragMessageTarget",
			Text = "Drag message target",
			Width = 240,
			Height = 180,
			ShowInTaskbar = false,
			StartPosition = Forms.FormStartPosition.Manual,
			Left = 20,
			Top = 20,
		};
		using var cover = new Forms.Form
		{
			Text = "Cover",
			Width = form.Width,
			Height = form.Height,
			ShowInTaskbar = false,
			StartPosition = Forms.FormStartPosition.Manual,
			Left = form.Left,
			Top = form.Top,
			TopMost = true,
			Opacity = 0.01,
		};

		try
		{
			form.Show();
			cover.Show();
			cover.Activate();
			Forms.Application.DoEvents();
			var source = form.PointToScreen(new System.Drawing.Point(30, 30));
			var destination = form.PointToScreen(new System.Drawing.Point(120, 80));
			var plan = new DragPlan(
				new PointerTarget(source.X, source.Y, form.Handle, "recording form"),
				new PointerTarget(destination.X, destination.Y, form.Handle, "recording form"),
				durationMs: 40,
				holdMs: 0,
				stepIntervalMs: 10,
				postDropWaitMs: 0,
				ensureForeground: true,
				validateSameProcess: true);

			var result = TargetMouseInput.PerformDragAndDrop(plan, CancellationToken.None);
			Forms.Application.DoEvents();

			Assert.That(result.Success, Is.True, result.Error);
			Assert.That(form.MouseMessages.Select(static message => message.Message), Does.Contain(NativeMethods.WM_LBUTTONDOWN));
			Assert.That(form.MouseMessages.Select(static message => message.Message), Does.Contain(NativeMethods.WM_MOUSEMOVE));
			Assert.That(form.MouseMessages.Select(static message => message.Message), Does.Contain(NativeMethods.WM_LBUTTONUP));
			var down = form.MouseMessages.First(message => message.Message == NativeMethods.WM_LBUTTONDOWN);
			var up = form.MouseMessages.Last(message => message.Message == NativeMethods.WM_LBUTTONUP);
			Assert.That(down.X, Is.EqualTo(30).Within(1));
			Assert.That(down.Y, Is.EqualTo(30).Within(1));
			Assert.That(up.X, Is.EqualTo(120).Within(1));
			Assert.That(up.Y, Is.EqualTo(80).Within(1));
		}
		finally
		{
			cover.Close();
			form.Close();
		}
	}

	[Test]
	public void NativeHwndKeyPressPostsKeyboardMessagesToTargetHandle()
	{
		using var form = CreateForm();
		var textBox = new Forms.TextBox { Name = "nativeKeyBox", Text = "abcdef", Width = 160 };
		form.Controls.Add(textBox);

		try
		{
			form.Show();
			Forms.Application.DoEvents();

			var snapshot = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = ["Name", "Text"],
				MaxNodeCount = 300,
			})!;
			var hwndNode = snapshot.Nodes.FirstOrDefault(node => node.TypeName == "HWND" && node.Hwnd == textBox.Handle.ToInt64());
			Assert.That(hwndNode, Is.Not.Null);

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = hwndNode!.TargetId, Keys = "Control+A", DelayMs = 1, EnsureForeground = false }));
			Assert.That(textBox.SelectionLength, Is.EqualTo(6));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = hwndNode.TargetId, Keys = "Backspace", DelayMs = 1, EnsureForeground = false }));
			Assert.That(textBox.Text, Is.Empty);
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void NativeAutomationFileNamePropertySetsValuePattern()
	{
		using var form = CreateForm();
		var textBox = new Forms.TextBox { Name = "automationFileNameBox", Width = 160 };
		form.Controls.Add(textBox);

		try
		{
			form.Show();
			Forms.Application.DoEvents();
			var automationElement = AutomationElement.FromHandle(textBox.Handle);
			var targetIds = new TargetIdService();
			var targetId = targetIds.GetOrCreateId(automationElement);

			AssertOk(InvokeSetProperty(
				new SetPropertyCommandRequest
				{
					TargetId = targetId,
					PropertyName = "FileName",
					PropertyValue = @"C:\temp\automation-selected.txt",
				},
				new TreeService(targetIds)));
			Forms.Application.DoEvents();

			Assert.That(textBox.Text, Is.EqualTo(@"C:\temp\automation-selected.txt"));
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void NativeDialogKnownOperationsInvokeAcceptAndCancelButtons()
	{
		using var form = CreateForm();
		var acceptCount = 0;
		var cancelCount = 0;
		var acceptButton = new Forms.Button { Name = "1", Text = "OK", Width = 80, Height = 28 };
		var cancelButton = new Forms.Button { Name = "2", Text = "Cancel", Left = 90, Width = 80, Height = 28 };
		acceptButton.Click += (_, _) => acceptCount++;
		cancelButton.Click += (_, _) => cancelCount++;
		form.Controls.Add(acceptButton);
		form.Controls.Add(cancelButton);

		try
		{
			form.Show();
			Forms.Application.DoEvents();
			var targetIds = new TargetIdService();
			var targetId = targetIds.GetOrCreateId(form.Handle);
			var treeService = new TreeService(targetIds);

			AssertOk(InvokeKnownOperation(new KnownOperationCommandRequest { TargetId = targetId, Operation = "AcceptDialog" }, treeService));
			AssertOk(InvokeKnownOperation(new KnownOperationCommandRequest { TargetId = targetId, Operation = "CancelDialog" }, treeService));
			Forms.Application.DoEvents();

			Assert.That(acceptCount, Is.EqualTo(1));
			Assert.That(cancelCount, Is.EqualTo(1));
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void ModalNativeDialogFallbackDetectsRootsAndBuildsNativeTree()
	{
		using var form = CreateForm();

		try
		{
			form.Show();
			Forms.Application.DoEvents();
			using var roots = NativeDialogService.OverrideRootWindowsForTests(new[] { form.Handle });

			Assert.That(NativeDialogService.HasRootWindowsForCurrentProcess(), Is.True);
			var waitResult = AppDriverCommandDispatcher.WaitForShowDialogAsync(1000, CancellationToken.None).GetAwaiter().GetResult();
			var treeService = NativeDialogService.TryCreateTreeService();

			Assert.That(waitResult, Is.EqualTo(UiThreadRunResult.Pending));
			Assert.That(treeService, Is.Not.Null);
			var snapshot = treeService!.CaptureSnapshot(new TreeSnapshotOptions
			{
				RequestedPropertyNames = ["Text", "ClassName"],
				MaxNodeCount = 100,
			});
			Assert.That(snapshot.Nodes.Any(node => node.Hwnd == form.Handle.ToInt64()), Is.True);
		}
		finally
		{
			form.Close();
		}
	}

	// Regression: a native MessageBox/dialog pumps the message queue, so an untargeted WPF find
	// dispatched while it's open still completes (against the WPF tree, which lacks the native
	// window) and returns NoMatch -- winning the modal-watch race so the native fallback never
	// fires. The dispatcher must augment an empty untargeted find with a native-tree lookup when
	// native dialog roots exist, so the dialog is surfaced deterministically rather than by luck.
	[Test]
	public void UntargetedFindAugmentsEmptyWpfResultWithNativeDialog()
	{
		var wpfWindow = CreateWindow("WPF host", new Button { Name = "onlyWpfButton", Content = "WPF" });
		using var nativeDialog = CreateForm();
		nativeDialog.Text = $"Pending changes found {Guid.NewGuid():N}";

		try
		{
			wpfWindow.Show();
			nativeDialog.Show();
			Forms.Application.DoEvents();
			using var roots = NativeDialogService.OverrideRootWindowsForTests(new[] { nativeDialog.Handle });

			// Dispatch on a worker thread while this STA thread pumps messages -- mirroring production,
			// where the command loop runs off the UI thread and the UI thread pumps the modal loop.
			// Running the dispatcher synchronously on the STA thread instead would deadlock, because the
			// native-tree augmentation uses UI Automation against a window owned by this same thread,
			// which only services UIA cross-thread calls while it is pumping.
			var request = new FindElementCommandRequest
			{
				Selector = new ElementSelectorDto { Properties = { ["Title"] = nativeDialog.Text } },
				PropNames = ["Title", "IsVisible"],
				MaxMatches = 1,
			};
			FindElementCommandResponse? response = null;
			var findTask = System.Threading.Tasks.Task.Run(
				() => response = (FindElementCommandResponse)CaptureResponse(request)!);
			while (!findTask.IsCompleted)
			{
				Forms.Application.DoEvents();
				Thread.Sleep(10);
			}

			findTask.GetAwaiter().GetResult();
			Assert.That(response, Is.Not.Null);
			Assert.That(response!.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(response.MatchCount, Is.EqualTo(1));
			Assert.That(response.Matches[0].Properties["Title"], Is.EqualTo(nativeDialog.Text));
		}
		finally
		{
			nativeDialog.Close();
			wpfWindow.Close();
		}
	}

	// Counterpart: when no native dialog roots exist, an empty untargeted find must stay NoMatch.
	// The augmentation must not invent matches or change behavior on the normal path.
	[Test]
	public void UntargetedFindWithoutNativeDialogStaysNoMatch()
	{
		var wpfWindow = CreateWindow("WPF host", new Button { Name = "onlyWpfButton", Content = "WPF" });

		try
		{
			wpfWindow.Show();
			Forms.Application.DoEvents();
			using var roots = NativeDialogService.OverrideRootWindowsForTests(Array.Empty<IntPtr>());

			var response = (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
			{
				Selector = new ElementSelectorDto { Properties = { ["Title"] = "no such dialog" } },
				PropNames = ["Title", "IsVisible"],
				MaxMatches = 1,
			})!;

			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.NoMatch));
			Assert.That(response.MatchCount, Is.EqualTo(0));
		}
		finally
		{
			wpfWindow.Close();
		}
	}

	[Test]
	public void HybridWpfWinFormsHostAppearsInSnapshots()
	{
		var root = new StackPanel { Name = "hybridRoot" };
		var host = new WindowsFormsHost
		{
			Child = new Forms.Button { Name = "hostedFormsButton", Text = "Hosted", Width = 90, Height = 28 },
		};
		root.Children.Add(host);
		host.Child.CreateControl();

		var targetIds = new TargetIdService();
		var rootId = targetIds.GetOrCreateId(root);
		var snapshot = new TreeService(targetIds).CaptureSnapshot(new TreeSnapshotOptions
		{
			RootTargetId = rootId,
			RequestedPropertyNames = ["Name", "Text", "Title"],
			MaxNodeCount = 300,
		});

		Assert.That(snapshot.Nodes.Any(node => node.Properties.TryGetValue("Name", out var value) && Equals(value, "hostedFormsButton")), Is.True);
		Assert.That(snapshot.TargetFrameworkFamily, Is.EqualTo("mixed"));
	}

	[Test]
	public void HybridWinFormsElementHostAppearsInSnapshots()
	{
		using var form = new Forms.Form { Name = "elementHostForm" };
		using var host = new ElementHost { Name = "wpfIsland" };
		host.Child = new Button { Name = "hostedWpfButton", Content = "Hosted WPF" };
		form.Controls.Add(host);

		var targetIds = new TargetIdService();
		var rootId = targetIds.GetOrCreateId(form);
		var snapshot = new TreeService(targetIds).CaptureSnapshot(new TreeSnapshotOptions
		{
			RootTargetId = rootId,
			RequestedPropertyNames = ["Name", "Content"],
			MaxNodeCount = 300,
		});

		var hostNode = snapshot.Nodes.Single(node => node.Properties.TryGetValue("Name", out var value) && Equals(value, "wpfIsland"));
		var wpfNode = snapshot.Nodes.Single(node => node.Properties.TryGetValue("Name", out var value) && Equals(value, "hostedWpfButton"));
		Assert.That(wpfNode.ParentId, Is.EqualTo(hostNode.TargetId));
		Assert.That(wpfNode.Properties["Content"], Is.EqualTo("Hosted WPF"));
		Assert.That(snapshot.TargetFrameworkFamily, Is.EqualTo("mixed"));
	}

	private static Forms.Form CreateForm()
	{
		return new Forms.Form
		{
			Name = "formsRoot",
			Text = "WinForms support",
			Width = 220,
			Height = 140,
			ShowInTaskbar = false,
			StartPosition = Forms.FormStartPosition.Manual,
			Left = 20,
			Top = 20,
		};
	}

	private static string FindTargetId(string name)
	{
		var response = (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
		{
			Selector = new ElementSelectorDto { Name = name },
			PropNames = ["Name", "Text"],
			MaxMatches = 1,
		})!;

		Assert.That(response.MatchCount, Is.EqualTo(1), name);
		return response.Matches[0].TargetId;
	}

	private static object? InvokeKnownOperation(KnownOperationCommandRequest request, TreeService treeService)
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");
		return TargetActionCommand.KnownOperation(request, treeService);
	}

	private static object? InvokeSetProperty(SetPropertyCommandRequest request, TreeService treeService)
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");
		return TargetActionCommand.SetProperty(request, treeService);
	}

	private static void AssertOk(object? response)
	{
		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		Assert.That(((StandardIpcResponse)response!).Success, Is.True, ((StandardIpcResponse)response).Error);
	}

	private sealed class RecordingDragForm : Forms.Form
	{
		public List<RecordedMouseMessage> MouseMessages { get; } = [];

		protected override void WndProc(ref Forms.Message m)
		{
			if (m.Msg is NativeMethods.WM_MOUSEMOVE or NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP)
			{
				MouseMessages.Add(new RecordedMouseMessage(
					m.Msg,
					unchecked((short)((long)m.LParam & 0xffff)),
					unchecked((short)(((long)m.LParam >> 16) & 0xffff))));
			}

			base.WndProc(ref m);
		}
	}

	private sealed record RecordedMouseMessage(int Message, int X, int Y);

}
