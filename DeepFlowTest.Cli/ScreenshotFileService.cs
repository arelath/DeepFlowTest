namespace DeepFlowTest.Cli;

using System;
using System.IO;
using DeepFlowTest.Contracts;

public sealed class ScreenshotFileOptions
{
	public string? OutputPath { get; set; }

	public bool IncludeBase64 { get; set; }
}

public sealed class ScreenshotFileService
{
	public ScreenshotResultData Process(ScreenshotCommandResponse response, ScreenshotFileOptions options)
	{
		_ = response ?? throw new ArgumentNullException(nameof(response));
		_ = options ?? throw new ArgumentNullException(nameof(options));

		var format = NormalizeFormat(response.Format);
		var bytes = Convert.FromBase64String(response.BytesBase64 ?? string.Empty);
		string? outputPath = null;
		if (!string.IsNullOrWhiteSpace(options.OutputPath))
		{
			outputPath = NormalizeOutputPath(options.OutputPath!, format);
			var directory = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);
			File.WriteAllBytes(outputPath, bytes);
		}

		return new ScreenshotResultData
		{
			TargetId = response.TargetId,
			Format = format,
			Width = response.Width,
			Height = response.Height,
			ByteCount = response.ByteCount == 0 ? bytes.Length : response.ByteCount,
			OutputPath = outputPath,
			BytesBase64 = options.IncludeBase64 ? response.BytesBase64 : null,
		};
	}

	public static string NormalizeFormat(string? format)
	{
		return (format ?? "png").Trim().ToLowerInvariant() switch
		{
			"png" => "png",
			"bmp" => "bmp",
			"gif" => "gif",
			"jpg" => "jpeg",
			"jpeg" => "jpeg",
			_ => throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported image format '{format}'."),
		};
	}

	private static string NormalizeOutputPath(string outputPath, string format)
	{
		try
		{
			var normalized = Path.GetFullPath(outputPath);
			if (string.IsNullOrWhiteSpace(Path.GetExtension(normalized)))
				normalized += "." + format;
			return normalized;
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			throw new CliException(CliErrorCodes.InvalidArguments, $"Invalid screenshot output path: {ex.Message}");
		}
	}
}

public sealed class ScreenshotResultData
{
	public string TargetId { get; set; } = string.Empty;

	public string Format { get; set; } = "png";

	public int Width { get; set; }

	public int Height { get; set; }

	public int ByteCount { get; set; }

	public string? OutputPath { get; set; }

	public string? BytesBase64 { get; set; }
}
