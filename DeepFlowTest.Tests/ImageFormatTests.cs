namespace DeepFlowTest.Tests;

using System;
using NUnit.Framework;

[TestFixture]
public sealed class ImageFormatTests
{
	[TestCase(null, ImageFormat.Png, "png")]
	[TestCase("png", ImageFormat.Png, "png")]
	[TestCase("bmp", ImageFormat.Bmp, "bmp")]
	[TestCase("gif", ImageFormat.Gif, "gif")]
	[TestCase("jpg", ImageFormat.Jpeg, "jpeg")]
	[TestCase("jpeg", ImageFormat.Jpeg, "jpeg")]
	public void ProtocolImageFormatParserNormalizesSupportedNames(string? input, ImageFormat expectedFormat, string expectedProtocolName)
	{
		var parsed = ImageFormatExtensions.ParseProtocolString(input);

		Assert.That(parsed, Is.EqualTo(expectedFormat));
		Assert.That(parsed.ToProtocolString(), Is.EqualTo(expectedProtocolName));
	}

	[Test]
	public void ProtocolImageFormatParserRejectsUnsupportedNames()
	{
		Assert.That(ImageFormatExtensions.TryParseProtocolString("tiff", out _), Is.False);
		Assert.That(() => ImageFormatExtensions.ParseProtocolString("tiff"), Throws.TypeOf<FormatException>());
	}
}
