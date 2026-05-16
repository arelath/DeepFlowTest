namespace DeepFlowTest.Cli;

using System;
using DeepFlowTest.Contracts;

internal enum CliClickButton
{
	Left,
	Right,
	Middle,
	Double,
}

internal static class CliValueParser
{
	public static ImageFormat ParseImageFormat(string? value)
	{
		try
		{
			return ImageFormatExtensions.ParseProtocolString(value);
		}
		catch (FormatException)
		{
			throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported image format '{value}'.");
		}
	}

	public static TreeShape ParseTreeShape(string? value)
	{
		try
		{
			return ProtocolValueMapper.ParseTreeShape(value);
		}
		catch (FormatException)
		{
			throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported tree shape '{value}'.");
		}
	}

	public static MouseButtonKind ParseMouseButton(string? value)
	{
		try
		{
			return ProtocolValueMapper.ParseMouseButton(value);
		}
		catch (FormatException)
		{
			throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported mouse button '{value}'.");
		}
	}

	public static CliClickButton ParseClickButton(string? value)
	{
		if (string.Equals(value, "double", StringComparison.OrdinalIgnoreCase))
			return CliClickButton.Double;

		return ParseMouseButton(value) switch
		{
			MouseButtonKind.Right => CliClickButton.Right,
			MouseButtonKind.Middle => CliClickButton.Middle,
			_ => CliClickButton.Left,
		};
	}

	public static MouseButtonKind ToMouseButton(CliClickButton button) =>
		button switch
		{
			CliClickButton.Right => MouseButtonKind.Right,
			CliClickButton.Middle => MouseButtonKind.Middle,
			_ => MouseButtonKind.Left,
		};

	public static string FormatClickButton(CliClickButton button) =>
		button == CliClickButton.Double
			? "double"
			: ProtocolValueMapper.FormatMouseButton(ToMouseButton(button));
}
