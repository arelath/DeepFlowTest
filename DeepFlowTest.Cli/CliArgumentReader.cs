namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

internal static class CliArgumentReader
{
	public static bool HasOption(IReadOnlyList<string> args, string name) =>
		args.Any(arg => string.Equals(arg, name, StringComparison.Ordinal) || arg.StartsWith(name + "=", StringComparison.Ordinal));

	public static bool HasOption(IReadOnlyList<string> args, params string[] names) =>
		names.Any(name => HasOption(args, name));

	public static string? GetOption(IReadOnlyList<string> args, string name)
	{
		for (var i = 0; i < args.Count; i++)
		{
			var arg = args[i];
			if (arg.StartsWith(name + "=", StringComparison.Ordinal))
				return arg[(name.Length + 1)..];

			if (string.Equals(arg, name, StringComparison.Ordinal))
			{
				if (i + 1 >= args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal))
					throw new CliException(CliErrorCodes.InvalidArguments, $"Missing value for '{name}'.");

				return args[i + 1];
			}
		}

		return null;
	}

	public static string? GetOption(IReadOnlyList<string> args, params string[] names)
	{
		foreach (var name in names)
		{
			var value = GetOption(args, name);
			if (value is not null)
				return value;
		}

		return null;
	}

	public static int GetInt(IReadOnlyList<string> args, string name, int defaultValue)
	{
		var value = GetOption(args, name);
		if (value is null)
			return defaultValue;

		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Invalid integer value for '{name}'.");

		return parsed;
	}

	public static double GetDouble(IReadOnlyList<string> args, string name, double defaultValue)
	{
		var value = GetOption(args, name);
		if (value is null)
			return defaultValue;

		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Invalid number value for '{name}'.");

		return parsed;
	}

	public static bool GetBool(IReadOnlyList<string> args, string name, bool defaultValue)
	{
		for (var i = 0; i < args.Count; i++)
		{
			var arg = args[i];
			if (arg.StartsWith(name + "=", StringComparison.Ordinal))
				return ParseBool(name, arg[(name.Length + 1)..]);

			if (!string.Equals(arg, name, StringComparison.Ordinal))
				continue;

			if (i + 1 < args.Count && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
				return ParseBool(name, args[i + 1]);

			return true;
		}

		return defaultValue;
	}

	public static IReadOnlyList<string> GetStringList(IReadOnlyList<string> args, string name, IReadOnlyList<string> defaultValue)
	{
		var value = GetOption(args, name);
		if (string.IsNullOrWhiteSpace(value))
			return defaultValue;

		return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	public static IReadOnlyList<string> SplitCsv(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return [];

		return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct(StringComparer.Ordinal)
			.ToArray();
	}

	public static KeyValuePair<string, string>? GetKeyValue(IReadOnlyList<string> args, params string[] names)
	{
		foreach (var name in names)
		{
			var value = GetOption(args, name);
			if (string.IsNullOrWhiteSpace(value))
				continue;

			var separator = value.IndexOf('=');
			if (separator <= 0)
				throw new CliException(CliErrorCodes.InvalidArguments, $"Option '{name}' must use name=value.");

			return new KeyValuePair<string, string>(value[..separator], value[(separator + 1)..]);
		}

		return null;
	}

	private static bool ParseBool(string name, string value)
	{
		if (bool.TryParse(value, out var parsed))
			return parsed;

		throw new CliException(CliErrorCodes.InvalidArguments, $"Invalid boolean value for '{name}'.");
	}
}
