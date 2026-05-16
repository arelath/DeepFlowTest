namespace DeepFlowTest;

using System;
using DeepFlowTest.Contracts;

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
		ProtocolValueMapper.FormatImageFormat(format);

	public static ImageFormat ParseProtocolString(string? format)
	{
		return ProtocolValueMapper.ParseImageFormat(format);
	}

	public static bool TryParseProtocolString(string? format, out ImageFormat imageFormat)
	{
		return ProtocolValueMapper.TryParseImageFormat(format, out imageFormat);
	}
}
