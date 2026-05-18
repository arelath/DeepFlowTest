namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Shared;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using Forms = System.Windows.Forms;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using Point = System.Drawing.Point;
using ProtocolImageFormat = DeepFlowTest.ImageFormat;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

internal static class ScreenshotCommand
{
	public static object Process(ScreenshotCommandRequest request, TreeService treeService)
	{
		_ = request ?? throw new ArgumentNullException(nameof(request));
		_ = treeService ?? throw new ArgumentNullException(nameof(treeService));

		var format = request.Format;
		if (!Enum.IsDefined(typeof(ProtocolImageFormat), format))
		{
			return StandardIpcResponse.FromError(
				$"Unsupported screenshot format '{format}'.",
				ProtocolConstants.ErrorCodes.ProtocolError,
				PayloadLog.CurrentCorrelationId);
		}

		var resolution = ResolveTarget(request, treeService, out var targetId);
		if (resolution.Status != TargetIdResolutionStatus.Found)
		{
			var errorCode = resolution.Status == TargetIdResolutionStatus.Stale
				? ProtocolConstants.ErrorCodes.StaleTarget
				: ProtocolConstants.ErrorCodes.UnsupportedTarget;
			return StandardIpcResponse.FromError($"Target '{targetId}' resolved as {resolution.Status}.", errorCode, PayloadLog.CurrentCorrelationId);
		}

		if (!TryCapture(resolution.Target!, format, out var capture, out var error))
			return StandardIpcResponse.FromError(error ?? "Target cannot be captured.", ProtocolConstants.ErrorCodes.UnsupportedTarget, PayloadLog.CurrentCorrelationId);

		return new ScreenshotCommandResponse
		{
			TargetId = targetId ?? resolution.TargetId,
			Format = format,
			Width = capture.Width,
			Height = capture.Height,
			ByteCount = capture.Bytes.Length,
			BytesBase64 = Convert.ToBase64String(capture.Bytes),
		};
	}

	private static TargetIdResolution ResolveTarget(ScreenshotCommandRequest request, TreeService treeService, out string? targetId)
	{
		targetId = request.TargetId;
		var requestedTargetId = targetId;
		if (!string.IsNullOrWhiteSpace(requestedTargetId))
			return treeService.ResolveTarget(requestedTargetId!);

		var snapshot = treeService.CaptureSnapshot(new TreeSnapshotOptions
		{
			RequestedPropertyNames = [],
			MaxNodeCount = 64,
		});
		targetId = SelectDefaultScreenshotTargetId(snapshot);
		return string.IsNullOrWhiteSpace(targetId)
			? TargetIdResolution.NotFound(string.Empty)
			: treeService.ResolveTarget(targetId!);
	}

	private static string? SelectDefaultScreenshotTargetId(VisualTreeSnapshot snapshot)
	{
		var rootCandidate = snapshot.Nodes.FirstOrDefault(node => snapshot.RootIds.Contains(node.TargetId) && CanCaptureNode(node));
		if (rootCandidate is not null)
			return rootCandidate.TargetId;

		return snapshot.Nodes.FirstOrDefault(CanCaptureNode)?.TargetId
			?? snapshot.RootIds.FirstOrDefault();
	}

	private static bool CanCaptureNode(VisualTreeNodeDto node)
	{
		if (node.Hwnd.HasValue)
			return true;

		return string.Equals(node.TargetKind, TargetObjectKind.WpfVisual.ToString(), StringComparison.Ordinal)
			|| string.Equals(node.TargetKind, TargetObjectKind.WinFormsControl.ToString(), StringComparison.Ordinal)
			|| string.Equals(node.TargetKind, TargetObjectKind.NativeWindow.ToString(), StringComparison.Ordinal)
			|| string.Equals(node.TargetKind, TargetObjectKind.NativeAutomationElement.ToString(), StringComparison.Ordinal)
			|| string.Equals(node.TargetKind, TargetObjectKind.Image.ToString(), StringComparison.Ordinal);
	}

	private static bool TryCapture(object target, ProtocolImageFormat format, out ScreenshotCapture capture, out string? error)
	{
		if (target is Window window)
			return TryCaptureWpfWindow(window, format, out capture, out error);

		if (target is Visual visual)
			return TryCaptureWpfVisual(visual, format, out capture, out error);

		if (target is Forms.Control control)
			return TryCaptureWinFormsControl(control, format, out capture, out error);

		if (target is IntPtr hwnd)
			return TryCaptureNativeWindow(hwnd, format, out capture, out error);

		if (target is AutomationElement automationElement)
			return TryCaptureAutomationElement(automationElement, format, out capture, out error);

		capture = ScreenshotCapture.Empty;
		error = $"Target type '{target.GetType().FullName}' does not support screenshots.";
		return false;
	}

	private static bool TryCaptureWpfWindow(Window window, ProtocolImageFormat format, out ScreenshotCapture capture, out string? error)
	{
		if (TryCaptureWpfWindowContent(window, format, out capture, out error))
			return true;

		if (TryCaptureWpfVisual(window, format, out capture, out error))
			return true;

		var hwnd = new WindowInteropHelper(window).Handle;
		if (hwnd != IntPtr.Zero && TryCaptureNativeWindow(hwnd, format, out capture, out error))
			return true;

		return false;
	}

	private static bool TryCaptureWpfWindowContent(Window window, ProtocolImageFormat format, out ScreenshotCapture capture, out string? error)
	{
		if (window.Content is not Visual contentVisual)
		{
			capture = ScreenshotCapture.Empty;
			error = "WPF window has no visual content.";
			return false;
		}

		if (contentVisual is FrameworkElement contentElement
			&& (GetRenderableWidth(contentElement) <= 0 || GetRenderableHeight(contentElement) <= 0))
		{
			window.UpdateLayout();
			var width = GetRenderableWidth(window);
			var height = GetRenderableHeight(window);
			if (width > 0 && height > 0)
			{
				var size = new System.Windows.Size(width, height);
				contentElement.Measure(size);
				contentElement.Arrange(new Rect(size));
				contentElement.UpdateLayout();
			}
		}

		return TryCaptureWpfVisual(contentVisual, format, out capture, out error);
	}

	private static bool TryCaptureWpfVisual(Visual visual, ProtocolImageFormat format, out ScreenshotCapture capture, out string? error)
	{
		if (visual is FrameworkElement frameworkElement)
			EnsureWpfLayout(frameworkElement);

		var width = 0;
		var height = 0;
		if (visual is FrameworkElement element)
		{
			width = (int)Math.Ceiling(GetRenderableWidth(element));
			height = (int)Math.Ceiling(GetRenderableHeight(element));
		}

		if (width <= 0 || height <= 0)
		{
			var bounds = VisualTreeHelper.GetDescendantBounds(visual);
			width = (int)Math.Ceiling(bounds.Width);
			height = (int)Math.Ceiling(bounds.Height);
		}

		if (width <= 0 || height <= 0)
		{
			capture = ScreenshotCapture.Empty;
			error = "WPF target has no renderable size.";
			return false;
		}

		var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
		bitmap.Render(visual);
		var bytes = EncodeBitmapSource(bitmap, format);
		capture = new ScreenshotCapture(width, height, bytes);
		error = null;
		return true;
	}

	private static void EnsureWpfLayout(FrameworkElement element)
	{
		Window.GetWindow(element)?.UpdateLayout();
		element.UpdateLayout();

		if (element is Window)
			return;

		if (GetRenderableWidth(element) > 0 && GetRenderableHeight(element) > 0)
			return;

		element.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
		if (element.DesiredSize.Width <= 0 || element.DesiredSize.Height <= 0)
			return;

		element.Arrange(new Rect(element.DesiredSize));
		element.UpdateLayout();
	}

	private static double GetRenderableWidth(FrameworkElement element) =>
		GetRenderableSize(element.ActualWidth, element.RenderSize.Width, element.Width);

	private static double GetRenderableHeight(FrameworkElement element) =>
		GetRenderableSize(element.ActualHeight, element.RenderSize.Height, element.Height);

	private static double GetRenderableSize(double actual, double render, double explicitSize)
	{
		if (actual > 0)
			return actual;
		if (render > 0)
			return render;
		return explicitSize > 0 && !double.IsInfinity(explicitSize) && !double.IsNaN(explicitSize)
			? explicitSize
			: 0;
	}

	private static bool TryCaptureWinFormsControl(Forms.Control control, ProtocolImageFormat format, out ScreenshotCapture capture, out string? error)
	{
		if (control.Width <= 0 || control.Height <= 0)
		{
			capture = ScreenshotCapture.Empty;
			error = "WinForms target has no renderable size.";
			return false;
		}

		using var bitmap = new Bitmap(control.Width, control.Height);
		control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, new Size(control.Width, control.Height)));
		var bytes = EncodeDrawingBitmap(bitmap, format);
		capture = new ScreenshotCapture(bitmap.Width, bitmap.Height, bytes);
		error = null;
		return true;
	}

	private static bool TryCaptureAutomationElement(AutomationElement automationElement, ProtocolImageFormat format, out ScreenshotCapture capture, out string? error)
	{
		try
		{
			var hwnd = new IntPtr(automationElement.Current.NativeWindowHandle);
			if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd) && TryCaptureNativeWindow(hwnd, format, out capture, out error))
				return true;

			var bounds = automationElement.Current.BoundingRectangle;
			return TryCaptureScreenBounds(
				new Rectangle((int)bounds.X, (int)bounds.Y, (int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height)),
				format,
				out capture,
				out error);
		}
		catch (ElementNotAvailableException ex)
		{
			capture = ScreenshotCapture.Empty;
			error = ex.Message;
			return false;
		}
	}

	private static bool TryCaptureNativeWindow(IntPtr hwnd, ProtocolImageFormat format, out ScreenshotCapture capture, out string? error)
	{
		if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
		{
			capture = ScreenshotCapture.Empty;
			error = "Native window handle is not available.";
			return false;
		}

		var bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
		if (TryCaptureWindowHandle(hwnd, bounds, format, out capture, out error))
			return true;

		return TryCaptureScreenBounds(
			bounds,
			format,
			out capture,
			out error);
	}

	private static bool TryCaptureWindowHandle(IntPtr hwnd, Rectangle bounds, ProtocolImageFormat format, out ScreenshotCapture capture, out string? error)
	{
		if (bounds.Width <= 0 || bounds.Height <= 0)
		{
			capture = ScreenshotCapture.Empty;
			error = "Native window has no renderable size.";
			return false;
		}

		try
		{
			using var bitmap = new Bitmap(bounds.Width, bounds.Height);
			using var graphics = Graphics.FromImage(bitmap);
			var hdc = graphics.GetHdc();
			try
			{
				if (!NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT)
					&& !NativeMethods.PrintWindow(hwnd, hdc, 0))
				{
					capture = ScreenshotCapture.Empty;
					error = "Native window render capture failed.";
					return false;
				}
			}
			finally
			{
				graphics.ReleaseHdc(hdc);
			}

			var bytes = EncodeDrawingBitmap(bitmap, format);
			capture = new ScreenshotCapture(bitmap.Width, bitmap.Height, bytes);
			error = null;
			return true;
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
		{
			capture = ScreenshotCapture.Empty;
			error = $"Native window render capture failed: {ex.Message}";
			return false;
		}
	}

	private static bool TryCaptureScreenBounds(Rectangle bounds, ProtocolImageFormat format, out ScreenshotCapture capture, out string? error)
	{
		if (bounds.Width <= 0 || bounds.Height <= 0)
		{
			capture = ScreenshotCapture.Empty;
			error = "Screen bounds have no renderable size.";
			return false;
		}

		try
		{
			using var bitmap = new Bitmap(bounds.Width, bounds.Height);
			using (var graphics = Graphics.FromImage(bitmap))
				graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

			var bytes = EncodeDrawingBitmap(bitmap, format);
			capture = new ScreenshotCapture(bitmap.Width, bitmap.Height, bytes);
			error = null;
			return true;
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
		{
			capture = ScreenshotCapture.Empty;
			error = $"Screen capture failed: {ex.Message}";
			return false;
		}
	}

	private static byte[] EncodeBitmapSource(BitmapSource bitmap, ProtocolImageFormat format)
	{
		BitmapEncoder encoder = format switch
		{
			ProtocolImageFormat.Bmp => new BmpBitmapEncoder(),
			ProtocolImageFormat.Gif => new GifBitmapEncoder(),
			ProtocolImageFormat.Jpeg => new JpegBitmapEncoder(),
			_ => new PngBitmapEncoder(),
		};
		encoder.Frames.Add(BitmapFrame.Create(bitmap));
		using var stream = new MemoryStream();
		encoder.Save(stream);
		return stream.ToArray();
	}

	private static byte[] EncodeDrawingBitmap(Bitmap bitmap, ProtocolImageFormat format)
	{
		var imageFormat = format switch
		{
			ProtocolImageFormat.Bmp => DrawingImageFormat.Bmp,
			ProtocolImageFormat.Gif => DrawingImageFormat.Gif,
			ProtocolImageFormat.Jpeg => DrawingImageFormat.Jpeg,
			_ => DrawingImageFormat.Png,
		};
		using var stream = new MemoryStream();
		bitmap.Save(stream, imageFormat);
		return stream.ToArray();
	}

	private readonly struct ScreenshotCapture
	{
		public static readonly ScreenshotCapture Empty = new(0, 0, []);

		public ScreenshotCapture(int width, int height, byte[] bytes)
		{
			Width = width;
			Height = height;
			Bytes = bytes;
		}

		public int Width { get; }

		public int Height { get; }

		public byte[] Bytes { get; }
	}
}
