namespace DeepFlowTest;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DeepFlowTest.Contracts;

internal sealed class MediaCaptureService(DriverCommandClient commandClient)
{
	private static readonly object RecordingSync = new();
	private static IDisposable? activeRecording;
	private readonly DriverCommandClient commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));

	public static Func<ProcessStartInfo, IRecordingProcess> RecordingProcessFactory { get; set; } = ProcessRecordingProcess.Start;

	public static string? RecordingFfmpegPathOverride { get; set; }

	public ScreenshotCommandResponse CaptureScreenshot(string format = "png") =>
		commandClient.Send<ScreenshotCommandResponse>(new ScreenshotCommandRequest { Format = ImageFormatExtensions.ParseProtocolString(format) });

	public byte[] Screenshot(ImageFormat format = ImageFormat.Jpeg) =>
		DecodeScreenshot(WaitForStableScreenshot(() => commandClient.Send<ScreenshotCommandResponse>(new ScreenshotCommandRequest { Format = format }), nameof(Screenshot)));

	public void SaveScreenshot(string fileOutputPath)
	{
		_ = fileOutputPath ?? throw new ArgumentNullException(nameof(fileOutputPath));
		var bytes = Screenshot(GetImageFormatFromPath(fileOutputPath));
		WriteBytes(fileOutputPath, bytes);
	}

	public static IDisposable Record(string fileOutputPath, string? windowTitle = null)
	{
		_ = fileOutputPath ?? throw new ArgumentNullException(nameof(fileOutputPath));
		lock (RecordingSync)
		{
			activeRecording?.Dispose();
			activeRecording = null;

			fileOutputPath = Environment.ExpandEnvironmentVariables(fileOutputPath);
			fileOutputPath = Path.GetFullPath(fileOutputPath);
			var directory = Path.GetDirectoryName(fileOutputPath);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);
			if (File.Exists(fileOutputPath))
				File.Delete(fileOutputPath);

			var ffmpegPath = ResolveFfmpegPath();
			var fullScreen = string.IsNullOrEmpty(windowTitle) || Process.GetProcesses().Count(process => string.Equals(process.MainWindowTitle, windowTitle, StringComparison.Ordinal)) > 1;
			var arguments = fullScreen
				? $"-y -f gdigrab -framerate 24 -i desktop \"{fileOutputPath}\" -c:v vp8"
				: $"-y -f gdigrab -framerate 24 -i title=\"{EscapeFfmpegArgument(windowTitle!)}\" \"{fileOutputPath}\" -c:v vp8";

			var recorder = RecordingProcessFactory(new ProcessStartInfo
			{
				FileName = ffmpegPath,
				Arguments = arguments,
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				RedirectStandardInput = true,
			});
			try
			{
				recorder.RegisterForParentClose();
			}
			catch
			{
				recorder.Dispose();
				throw;
			}

			activeRecording = new RecordingScope(recorder, () =>
			{
				lock (RecordingSync)
				{
					activeRecording = null;
				}
			});
			return activeRecording;
		}
	}

	public static ScreenshotCommandResponse WaitForStableScreenshot(Func<ScreenshotCommandResponse> capture, string caller)
	{
		_ = capture ?? throw new ArgumentNullException(nameof(capture));
		var stopwatch = Stopwatch.StartNew();
		ScreenshotCommandResponse? previous = null;
		ScreenshotCommandResponse? current = null;

		while (stopwatch.ElapsedMilliseconds < TimeoutDefaults.ScreenshotStableTimeoutMs)
		{
			current = capture();
			ThrowIfScreenshotFailed(current, caller);
			if (previous is not null && string.Equals(previous.BytesBase64, current.BytesBase64, StringComparison.Ordinal))
				return current;

			previous = current;
			Thread.Sleep(TimeoutDefaults.ScreenshotStablePollDelayMs);
		}

		current ??= capture();
		ThrowIfScreenshotFailed(current, caller);
		return current;
	}

	public static byte[] DecodeScreenshot(ScreenshotCommandResponse response) =>
		Convert.FromBase64String(response.BytesBase64 ?? string.Empty);

	public static void WriteBytes(string fileOutputPath, byte[] bytes)
	{
		var directory = Path.GetDirectoryName(Path.GetFullPath(fileOutputPath));
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);
		File.WriteAllBytes(fileOutputPath, bytes);
	}

	public static ImageFormat GetImageFormatFromPath(string path)
	{
		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			".bmp" => ImageFormat.Bmp,
			".gif" => ImageFormat.Gif,
			".jpg" or ".jpeg" => ImageFormat.Jpeg,
			_ => ImageFormat.Png,
		};
	}

	private static void ThrowIfScreenshotFailed(ScreenshotCommandResponse response, string caller)
	{
		if (response.Status == ProtocolConstants.Statuses.PendingResult)
			throw new TimeoutException($"{caller} timeout.");
		if (response.Success == false)
			throw new AppDriverException(response.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, response.Error ?? $"{caller} failed.");
	}

	private static string ResolveFfmpegPath()
	{
		if (!string.IsNullOrWhiteSpace(RecordingFfmpegPathOverride))
			return RecordingFfmpegPathOverride!;

		var baseDirectory = AppContext.BaseDirectory;
		var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDirectory;
		var candidates = new[]
		{
			Path.Combine(baseDirectory, "DeepFlowTestResources", "ffmpeg.exe"),
			Path.Combine(assemblyDirectory, "DeepFlowTestResources", "ffmpeg.exe"),
			Path.Combine(baseDirectory, "contentFiles", "any", "any", "DeepFlowTestResources", "ffmpeg.exe"),
			Path.Combine(assemblyDirectory, "contentFiles", "any", "any", "DeepFlowTestResources", "ffmpeg.exe"),
		};

		var path = candidates.FirstOrDefault(File.Exists);
		if (path is not null)
			return path;

		throw new FileNotFoundException("FFmpeg was not found. Expected ffmpeg.exe under DeepFlowTestResources next to the DeepFlowTest assembly.", candidates[0]);
	}

	private static string EscapeFfmpegArgument(string value) =>
		value.Replace("\"", "\\\"");
}
