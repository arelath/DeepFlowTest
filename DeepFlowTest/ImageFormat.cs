namespace DeepFlowTest;

using System;

public enum ImageFormat
{
	Png,
	Jpeg,
	Bmp,
	Gif,
}

public static class ImageFormatExtensions
{
	public static string ToProtocolString(this ImageFormat format) =>
		format switch
		{
			ImageFormat.Bmp => "bmp",
			ImageFormat.Gif => "gif",
			ImageFormat.Jpeg => "jpeg",
			_ => "png",
		};

	public static ImageFormat ParseProtocolString(string? format)
	{
		if (TryParseProtocolString(format, out var imageFormat))
			return imageFormat;

		throw new FormatException($"Unsupported image format '{format}'.");
	}

	public static bool TryParseProtocolString(string? format, out ImageFormat imageFormat)
	{
		switch ((format ?? "png").Trim().ToLowerInvariant())
		{
			case "png":
				imageFormat = ImageFormat.Png;
				return true;
			case "bmp":
				imageFormat = ImageFormat.Bmp;
				return true;
			case "gif":
				imageFormat = ImageFormat.Gif;
				return true;
			case "jpg":
			case "jpeg":
				imageFormat = ImageFormat.Jpeg;
				return true;
			default:
				imageFormat = default;
				return false;
		}
	}
}
