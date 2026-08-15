namespace DeepFlowTest.Mcp.Contracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeepFlowTest;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;

internal static class McpArgumentParsing
{
	public static IReadOnlyList<string> ParseProperties(string? properties, IReadOnlyList<string> fallback)
	{
		if (string.IsNullOrWhiteSpace(properties) || string.Equals(properties, "default", StringComparison.OrdinalIgnoreCase))
			return fallback;

		if (string.Equals(properties, "none", StringComparison.OrdinalIgnoreCase))
			return [];

		return properties
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(static property => !string.IsNullOrWhiteSpace(property))
			.Distinct(StringComparer.Ordinal)
			.ToArray();
	}

	public static KeyValuePair<string, string>? ParsePair(string? value, string argumentName)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;

		var separator = value.IndexOf('=');
		if (separator <= 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"{argumentName} must use name=value.");

		return new KeyValuePair<string, string>(value[..separator], value[(separator + 1)..]);
	}

	public static TreeShape ParseTreeShape(string? value, TreeShape fallback)
	{
		if (string.IsNullOrWhiteSpace(value))
			return fallback;

		try
		{
			return ProtocolValueMapper.ParseTreeShape(value);
		}
		catch (FormatException)
		{
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Unsupported tree shape '{value}'.");
		}
	}

	public static MouseButtonKind ParseMouseButton(string? value, MouseButtonKind fallback)
	{
		if (string.IsNullOrWhiteSpace(value))
			return fallback;

		try
		{
			return ProtocolValueMapper.ParseMouseButton(value);
		}
		catch (FormatException)
		{
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Unsupported mouse button '{value}'.");
		}
	}

	public static ImageFormat ParseImageFormat(string? value, ImageFormat fallback)
	{
		if (string.IsNullOrWhiteSpace(value))
			return fallback;

		try
		{
			return ImageFormatExtensions.ParseProtocolString(value);
		}
		catch (FormatException)
		{
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Unsupported image format '{value}'.");
		}
	}

	public static object? ParseJsonScalar(string value)
	{
		try
		{
			using var document = JsonDocument.Parse(value);
			return document.RootElement.ValueKind switch
			{
				JsonValueKind.String => document.RootElement.GetString(),
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Number when document.RootElement.TryGetInt64(out var longValue) => longValue,
				JsonValueKind.Number => document.RootElement.GetDouble(),
				JsonValueKind.Null => null,
				_ => throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Only JSON scalar values are supported."),
			};
		}
		catch (JsonException)
		{
			return value;
		}
	}

	public static void ValidateExecutableAllowed(string fileName, IReadOnlyList<string> allowedRoots)
	{
		if (allowedRoots.Count == 0)
			return;

		var fullPath = Path.GetFullPath(fileName);
		foreach (var root in allowedRoots)
		{
			if (string.IsNullOrWhiteSpace(root))
				continue;

			var fullRoot = Path.GetFullPath(root);
			if (fullPath.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
				return;
		}

		throw new AutomationException(AutomationErrorCodes.ActionDenied, $"Launch path '{fullPath}' is outside the allowed executable roots.");
	}
}
