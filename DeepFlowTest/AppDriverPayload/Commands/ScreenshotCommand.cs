namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using Forms = System.Windows.Forms;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

internal static class ScreenshotCommand
{
	public static object Process(ScreenshotCommandRequest request, TreeService treeService)
	{
		_ = request ?? throw new ArgumentNullException(nameof(request));
		_ = treeService ?? throw new ArgumentNullException(nameof(treeService));

		var format = NormalizeFormat(request.Format);
		if (format is null)
		{
			return StandardIpcResponse.FromError(
				$"Unsupported screenshot format '{request.Format}'.",
				ProtocolConstants.ErrorCodes.ProtocolError,
				LogCorrelationId());
		}

		var resolution = ResolveTarget(request, treeService, out var targetId);
		if (resolution.Status != TargetIdResolutionStatus.Found)
		{
			var errorCode = resolution.Status == TargetIdResolutionStatus.Stale
				? ProtocolConstants.ErrorCodes.StaleTarget
				: ProtocolConstants.ErrorCodes.UnsupportedTarget;
			return StandardIpcResponse.FromError($"Target '{targetId}' resolved as {resolution.Status}.", errorCode, LogCorrelationId());
		}

		if (!TryCapture(resolution.Target!, format, out var capture, out var error))
			return StandardIpcResponse.FromError(error ?? "Target cannot be captured.", ProtocolConstants.ErrorCodes.UnsupportedTarget, LogCorrelationId());

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
			RequestedPropertyNames = Array.Empty<string>(),
			MaxNodeCount = 1,
		});
		targetId = snapshot.RootIds.FirstOrDefault();
		return string.IsNullOrWhiteSpace(targetId)
			? TargetIdResolution.NotFound(string.Empty)
			: treeService.ResolveTarget(targetId);
	}

	private static bool TryCapture(object target, string format, out ScreenshotCapture capture, out string? error)
	{
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

	private static bool TryCaptureWpfVisual(Visual visual, string format, out ScreenshotCapture capture, out string? error)
	{
		if (visual is FrameworkElement frameworkElement)
			frameworkElement.UpdateLayout();

		var width = 0;
		var height = 0;
		if (visual is FrameworkElement element)
		{
			width = (int)Math.Ceiling(element.ActualWidth > 0 ? element.ActualWidth : element.RenderSize.Width);
			height = (int)Math.Ceiling(element.ActualHeight > 0 ? element.ActualHeight : element.RenderSize.Height);
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

	private static bool TryCaptureWinFormsControl(Forms.Control control, string format, out ScreenshotCapture capture, out string? error)
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

	private static bool TryCaptureAutomationElement(AutomationElement automationElement, string format, out ScreenshotCapture capture, out string? error)
	{
		try
		{
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

	private static bool TryCaptureNativeWindow(IntPtr hwnd, string format, out ScreenshotCapture capture, out string? error)
	{
		if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
		{
			capture = ScreenshotCapture.Empty;
			error = "Native window handle is not available.";
			return false;
		}

		return TryCaptureScreenBounds(
			new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
			format,
			out capture,
			out error);
	}

	private static bool TryCaptureScreenBounds(Rectangle bounds, string format, out ScreenshotCapture capture, out string? error)
	{
		if (bounds.Width <= 0 || bounds.Height <= 0)
		{
			capture = ScreenshotCapture.Empty;
			error = "Screen bounds have no renderable size.";
			return false;
		}

		using var bitmap = new Bitmap(bounds.Width, bounds.Height);
		using (var graphics = Graphics.FromImage(bitmap))
			graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

		var bytes = EncodeDrawingBitmap(bitmap, format);
		capture = new ScreenshotCapture(bitmap.Width, bitmap.Height, bytes);
		error = null;
		return true;
	}

	private static byte[] EncodeBitmapSource(BitmapSource bitmap, string format)
	{
		BitmapEncoder encoder = format switch
		{
			"bmp" => new BmpBitmapEncoder(),
			"gif" => new GifBitmapEncoder(),
			"jpeg" => new JpegBitmapEncoder(),
			_ => new PngBitmapEncoder(),
		};
		encoder.Frames.Add(BitmapFrame.Create(bitmap));
		using var stream = new MemoryStream();
		encoder.Save(stream);
		return stream.ToArray();
	}

	private static byte[] EncodeDrawingBitmap(Bitmap bitmap, string format)
	{
		var imageFormat = format switch
		{
			"bmp" => ImageFormat.Bmp,
			"gif" => ImageFormat.Gif,
			"jpeg" => ImageFormat.Jpeg,
			_ => ImageFormat.Png,
		};
		using var stream = new MemoryStream();
		bitmap.Save(stream, imageFormat);
		return stream.ToArray();
	}

	private static string? NormalizeFormat(string? format)
	{
		return (format ?? "png").Trim().ToLowerInvariant() switch
		{
			"png" => "png",
			"bmp" => "bmp",
			"gif" => "gif",
			"jpg" => "jpeg",
			"jpeg" => "jpeg",
			_ => null,
		};
	}

	private static string LogCorrelationId()
	{
		return System.IO.Path.GetFileNameWithoutExtension(PayloadLog.CurrentLogPath);
	}

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

	private struct NativeRect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	private readonly struct ScreenshotCapture
	{
		public static readonly ScreenshotCapture Empty = new(0, 0, Array.Empty<byte>());

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
