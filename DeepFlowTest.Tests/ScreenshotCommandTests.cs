namespace DeepFlowTest.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using NUnit.Framework;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;
using static DeepFlowTest.Tests.TestIpcHost;
using static DeepFlowTest.Tests.WpfTestHelpers;

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

			var response = (ScreenshotCommandResponse)CaptureResponse(new ScreenshotCommandRequest { Format = ImageFormat.Png })!;

			Assert.That(response.Format, Is.EqualTo(ImageFormat.Png));
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
	public void DefaultScreenshotSkipsApplicationRootAndCapturesWindow()
	{
		_ = Application.Current ?? new Application();
		var window = CreateWindow("Screenshot application root", new Button { Name = "screenshotButton", Content = "Capture" });

		try
		{
			window.Show();
			var treeService = new TreeService(rootProvider: () => [Application.Current!]);

			var response = (ScreenshotCommandResponse)InvokeScreenshotProcess(new ScreenshotCommandRequest { Format = ImageFormat.Png }, treeService)!;

			Assert.That(response.TargetId, Is.Not.Empty);
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
	public void ElementScreenshotIncludesTargetMetadata()
	{
		var window = CreateWindow("Element screenshot", new Button { Name = "elementButton", Content = "Element" });

		try
		{
			window.Show();
			var snapshot = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = [KnownProperties.Name, KnownProperties.Content],
				MaxNodeCount = 200,
			})!;
			var buttonNode = snapshot.Nodes.Single(node =>
				node.Properties.TryGetValue(KnownProperties.Name, out var value) && Equals(value, "elementButton"));

			var responseObject = CaptureResponse(new ScreenshotCommandRequest
			{
				TargetId = buttonNode.TargetId,
				Format = ImageFormat.Jpeg,
			});
			Assert.That(responseObject, Is.TypeOf<ScreenshotCommandResponse>(), responseObject?.ToString());
			var response = (ScreenshotCommandResponse)responseObject!;

			Assert.That(response.TargetId, Is.EqualTo(buttonNode.TargetId));
			Assert.That(response.Format, Is.EqualTo(ImageFormat.Jpeg));
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
	public void WpfWindowScreenshotCapturesWindowContentWhenCovered()
	{
		var targetIds = new TargetIdService();
		var target = CreateWindow(
			"Covered screenshot target",
			new Border { Background = Brushes.Red },
			width: 180,
			height: 140);
		var cover = CreateWindow(
			"Covered screenshot overlay",
			new Border { Background = Brushes.Blue },
			width: 180,
			height: 140);
		target.Left = 120;
		target.Top = 120;
		cover.Left = target.Left;
		cover.Top = target.Top;
		cover.Topmost = true;

		try
		{
			target.Show();
			target.UpdateLayout();
			cover.Show();
			cover.Activate();
			cover.UpdateLayout();

			var targetId = targetIds.GetOrCreateId(target);
			var responseObject = InvokeScreenshotProcess(
				new ScreenshotCommandRequest
				{
					TargetId = targetId,
					Format = ImageFormat.Png,
				},
				new TreeService(targetIds));
			Assert.That(responseObject, Is.TypeOf<ScreenshotCommandResponse>(), responseObject?.ToString());
			var response = (ScreenshotCommandResponse)responseObject!;

			var centerPixel = GetCenterPixel(response);

			Assert.Multiple(() =>
			{
				Assert.That(centerPixel.R, Is.GreaterThan(180));
				Assert.That(centerPixel.G, Is.LessThan(100));
				Assert.That(centerPixel.B, Is.LessThan(100));
			});
		}
		finally
		{
			cover.Close();
			target.Close();
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
				Format = ImageFormat.Png,
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
			Format = ImageFormat.Png,
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

			var response = (ScreenshotCommandResponse)CaptureResponse(new ScreenshotCommandRequest { Format = ImageFormat.Png })!;

			Assert.That(response.Format, Is.EqualTo(ImageFormat.Png));
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

	private static object? InvokeScreenshotProcess(ScreenshotCommandRequest request, TreeService treeService)
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");
		return ScreenshotCommand.Process(request, treeService);
	}

	private static DrawingColor GetCenterPixel(ScreenshotCommandResponse response)
	{
		using var stream = new MemoryStream(Convert.FromBase64String(response.BytesBase64));
		using var bitmap = new DrawingBitmap(stream);
		return bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
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

}
