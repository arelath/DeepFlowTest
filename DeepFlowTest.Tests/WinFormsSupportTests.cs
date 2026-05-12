namespace DeepFlowTest.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using NUnit.Framework;
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

			var screenshot = (ScreenshotCommandResponse)CaptureResponse(new ScreenshotCommandRequest { TargetId = hwndNode!.TargetId, Format = "png" })!;
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
	public void ModalPendingCommandsUseNativeDialogTreeFallback()
	{
		using var form = CreateForm();

		try
		{
			form.Show();
			Forms.Application.DoEvents();
			using var roots = NativeDialogService.OverrideRootWindowsForTests(new[] { form.Handle });
			using var delay = AppDriverCommandDispatcher.DelayUiHandlersForTests(1000);

			var response = CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = ["Text", "ClassName"],
				TimeoutMs = 750,
			});

			Assert.That(response, Is.TypeOf<VisualTreeSnapshot>());
			var snapshot = (VisualTreeSnapshot)response!;
			Assert.That(snapshot.Nodes.Any(node => node.Hwnd == form.Handle.ToInt64()), Is.True);
		}
		finally
		{
			form.Close();
		}
	}

	[Test]
	public void HybridWpfWinFormsHostAppearsInSnapshots()
	{
		_ = Application.Current ?? new Application();
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

		AppDriverCommandDispatcher.Process(command, options, null);
		return response;
	}

}
