namespace DeepFlowTest.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using NUnit.Framework;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ScreenshotCommandTests
{
	[Test]
	public void AppScreenshotReturnsNonEmptyBytes()
	{
		var window = CreateWindow("Screenshot", new Button { Name = "screenshotButton", Content = "Capture" });

		try
		{
			window.Show();

			var response = (ScreenshotCommandResponse)CaptureResponse(new ScreenshotCommandRequest { Format = "png" })!;

			Assert.That(response.Format, Is.EqualTo("png"));
			Assert.That(response.TargetId, Is.Not.Empty);
			Assert.That(response.Width, Is.GreaterThan(0));
			Assert.That(response.Height, Is.GreaterThan(0));
			Assert.That(response.ByteCount, Is.GreaterThan(0));
			Assert.That(Convert.FromBase64String(response.BytesBase64).Length, Is.EqualTo(response.ByteCount));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ElementScreenshotIncludesTargetMetadata()
	{
		var window = CreateWindow("Element screenshot", new Button { Name = "elementButton", Content = "Element" });

		try
		{
			window.Show();
			var snapshot = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = new[] { "Name", "Content" },
				MaxNodeCount = 200,
			})!;
			var buttonNode = snapshot.Nodes.Single(node =>
				node.Properties.TryGetValue("Name", out var value) && Equals(value, "elementButton"));

			var response = (ScreenshotCommandResponse)CaptureResponse(new ScreenshotCommandRequest
			{
				TargetId = buttonNode.TargetId,
				Format = "jpg",
			})!;

			Assert.That(response.TargetId, Is.EqualTo(buttonNode.TargetId));
			Assert.That(response.Format, Is.EqualTo("jpeg"));
			Assert.That(response.Width, Is.GreaterThan(0));
			Assert.That(response.Height, Is.GreaterThan(0));
			Assert.That(response.ByteCount, Is.GreaterThan(0));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void MissingTargetReturnsUnsupportedTarget()
	{
		var window = CreateWindow("Missing target", new TextBlock { Text = "Root" });

		try
		{
			window.Show();

			var response = (StandardIpcResponse)CaptureResponse(new ScreenshotCommandRequest
			{
				TargetId = "dft-target-missing",
				Format = "png",
			})!;

			Assert.That(response.Success, Is.False);
			Assert.That(response.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.UnsupportedTarget));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void StaleTargetReturnsStaleTarget()
	{
		var targetIds = new TargetIdService();
		var targetId = CreateCollectableTargetId(targetIds, out var weakReference);
		ForceCollection(weakReference);
		var treeService = new TreeService(targetIds);

		var response = (StandardIpcResponse)InvokeScreenshotProcess(new ScreenshotCommandRequest
		{
			TargetId = targetId,
			Format = "png",
		}, treeService)!;

		Assert.That(response.Success, Is.False);
		Assert.That(response.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.StaleTarget));
	}

	[Test]
	public void WinFormsAppScreenshotReturnsNonEmptyBytes()
	{
		using var form = new Forms.Form
		{
			Text = "WinForms screenshot",
			Width = 220,
			Height = 140,
			StartPosition = Forms.FormStartPosition.Manual,
			Location = new DrawingPoint(-20000, -20000),
		};
		form.Controls.Add(new Forms.Button { Name = "winFormsButton", Text = "Capture", Width = 100, Height = 32 });

		try
		{
			form.Show();

			var response = (ScreenshotCommandResponse)CaptureResponse(new ScreenshotCommandRequest { Format = "png" })!;

			Assert.That(response.Format, Is.EqualTo("png"));
			Assert.That(response.TargetId, Is.Not.Empty);
			Assert.That(response.Width, Is.GreaterThan(0));
			Assert.That(response.Height, Is.GreaterThan(0));
			Assert.That(response.ByteCount, Is.GreaterThan(0));
		}
		finally
		{
			form.Close();
		}
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

	private static object? InvokeScreenshotProcess(ScreenshotCommandRequest request, TreeService treeService)
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");
		var commandType = Type.GetType("DeepFlowTest.AppDriverPayload.Commands.ScreenshotCommand, DeepFlowTest", throwOnError: true)!;
		var method = commandType.GetMethod("Process", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
		return method.Invoke(null, new object[] { request, treeService });
	}

	private static string CreateCollectableTargetId(TargetIdService service, out WeakReference weakReference)
	{
		var target = new object();
		weakReference = new WeakReference(target);
		return service.GetOrCreateId(target);
	}

	private static void ForceCollection(WeakReference weakReference)
	{
		for (var i = 0; i < 10 && weakReference.IsAlive; i++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}
	}

	private static Window CreateWindow(string title, object content)
	{
		return new Window
		{
			Title = title,
			Content = content,
			Width = 240,
			Height = 160,
			ShowInTaskbar = false,
			WindowStartupLocation = WindowStartupLocation.Manual,
			Left = -20000,
			Top = -20000,
		};
	}
}
