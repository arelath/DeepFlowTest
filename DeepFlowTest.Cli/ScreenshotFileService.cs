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

		if (!response.Success)
			throw new CliException(ProtocolErrorMapper.Map(response.ErrorCode), response.Error ?? "Screenshot command failed.", response);

		var format = response.Format;
		var formatName = format.ToProtocolString();
		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(response.BytesBase64 ?? string.Empty);
		}
		catch (FormatException ex)
		{
			throw new CliException(CliErrorCodes.ProtocolError, $"Screenshot payload bytes were malformed: {ex.Message}", response);
		}

		string? outputPath = null;
		if (!string.IsNullOrWhiteSpace(options.OutputPath))
		{
			outputPath = NormalizeOutputPath(options.OutputPath!, formatName);
			try
			{
				var directory = Path.GetDirectoryName(outputPath);
				if (!string.IsNullOrEmpty(directory))
					Directory.CreateDirectory(directory);
				File.WriteAllBytes(outputPath, bytes);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
			{
				throw new CliException(CliErrorCodes.InvalidArguments, $"Could not write screenshot output path: {ex.Message}");
			}
		}

		return new ScreenshotResultData
		{
			TargetId = response.TargetId,
			Format = formatName,
			Width = response.Width,
			Height = response.Height,
			ByteCount = response.ByteCount == 0 ? bytes.Length : response.ByteCount,
			OutputPath = outputPath,
			BytesBase64 = options.IncludeBase64 ? response.BytesBase64 : null,
		};
	}

	public static string NormalizeFormat(string? format)
	{
		return NormalizeImageFormat(format).ToProtocolString();
	}

	public static ImageFormat NormalizeImageFormat(string? format) =>
		CliValueParser.ParseImageFormat(format);

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
