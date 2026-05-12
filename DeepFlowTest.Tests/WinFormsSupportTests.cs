namespace DeepFlowTest.Tests;

using System;
using System.Linq;
using System.Threading;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
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
				PropNames = new[] { "Name", "Text" },
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
				PropNames = new[] { "Name", "Text" },
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
			PropNames = new[] { "Name", "Text" },
			MaxMatches = 1,
		})!;

		Assert.That(response.MatchCount, Is.EqualTo(1), name);
		return response.Matches[0].TargetId;
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

		var dispatcherType = Type.GetType("DeepFlowTest.AppDriverPayload.AppDriverCommandDispatcher, DeepFlowTest", throwOnError: true)!;
		var method = dispatcherType.GetMethod("Process", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
		method.Invoke(null, new object?[] { command, options, null });
		return response;
	}
}
