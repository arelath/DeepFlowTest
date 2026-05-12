namespace DeepFlowTest;

public enum ImageFormat
{
	Png,
	Jpeg,
	Bmp,
	Gif,
}

internal static class ImageFormatExtensions
{
	public static string ToProtocolString(this ImageFormat format) =>
		format switch
		{
			ImageFormat.Bmp => "bmp",
			ImageFormat.Gif => "gif",
			ImageFormat.Jpeg => "jpeg",
			_ => "png",
		};
}
