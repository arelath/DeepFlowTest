namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public sealed class CliCommonOptions
{
	public int? Pid { get; set; }

	public string? ProcessName { get; set; }

	public string? WindowTitle { get; set; }

	public int TimeoutMs { get; set; }

	public bool Debug { get; set; }

	public bool NoInject { get; set; }

	public string? PipeId { get; set; }

	public string Format { get; set; } = "json";

	public bool Pretty { get; set; }

	public bool HideEmpty { get; set; }

	public bool UseShortIds { get; set; }

	public bool AllowActions { get; set; }

	public bool AllowArbitraryInvoke { get; set; }

	public string After { get; set; } = "none";

	public int TargetSelectorCount =>
		(Pid.HasValue ? 1 : 0)
		+ (!string.IsNullOrWhiteSpace(ProcessName) ? 1 : 0)
		+ (!string.IsNullOrWhiteSpace(WindowTitle) ? 1 : 0);

	public static CliCommonOptions Parse(IReadOnlyList<string> args, CliDefaults defaults)
	{
		_ = defaults ?? throw new ArgumentNullException(nameof(defaults));
		var options = new CliCommonOptions
		{
			Pid = defaults.Common.Pid,
			ProcessName = defaults.Common.Process,
			WindowTitle = defaults.Common.WindowTitle,
			TimeoutMs = defaults.Common.TimeoutMs,
			Debug = defaults.Common.Debug,
			NoInject = defaults.Common.NoInject,
			PipeId = defaults.Common.PipeId,
			Format = defaults.Common.Format,
			Pretty = defaults.Common.Pretty,
			HideEmpty = defaults.Common.HideEmpty,
			UseShortIds = defaults.Common.UseShortIds,
			AllowActions = defaults.Common.AllowActions,
			AllowArbitraryInvoke = defaults.Common.AllowArbitraryInvoke,
			After = defaults.Common.After,
		};

		for (var i = 0; i < args.Count; i++)
		{
			var token = args[i];
			var optionValue = SplitOptionValue(token, out var optionName);
			switch (optionName)
			{
				case "--pid":
					options.Pid = ParseInt(ReadValue(args, ref i, "--pid", optionValue), "--pid");
					break;
				case "--process":
					options.ProcessName = ReadValue(args, ref i, "--process", optionValue);
					break;
				case "--window-title":
					options.WindowTitle = ReadValue(args, ref i, "--window-title", optionValue);
					break;
				case "--timeout-ms":
					options.TimeoutMs = ParseInt(ReadValue(args, ref i, "--timeout-ms", optionValue), "--timeout-ms");
					break;
				case "--debug":
					options.Debug = true;
					break;
				case "--no-inject":
					options.NoInject = true;
					break;
				case "--pipe-id":
					options.PipeId = ReadValue(args, ref i, "--pipe-id", optionValue);
					break;
				case "--format":
					options.Format = ReadValue(args, ref i, "--format", optionValue);
					break;
				case "--pretty":
					options.Pretty = true;
					break;
				case "--hide-empty":
					options.HideEmpty = true;
					break;
				case "--use-short-ids":
					options.UseShortIds = true;
					break;
				case "--allow-actions":
					options.AllowActions = true;
					break;
				case "--allow-arbitrary-invoke":
					options.AllowArbitraryInvoke = true;
					break;
				case "--after":
					options.After = ReadValue(args, ref i, "--after", optionValue);
					break;
			}
		}

		return options;
	}

	public void ValidateEnums()
	{
		Format = Format.ToLowerInvariant();
		After = After.ToLowerInvariant();
		if (!new[] { "json", "text" }.Contains(Format, StringComparer.Ordinal))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Invalid --format '{Format}'.");

		if (!new[] { "none", "target", "tree" }.Contains(After, StringComparer.Ordinal))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Invalid --after '{After}'.");
	}

	public void ValidateTargetSelectorRequired()
	{
		if (TargetSelectorCount == 0)
			throw new CliException(CliErrorCodes.InvalidArguments, "Exactly one target selector is required.");

		if (TargetSelectorCount > 1)
			throw new CliException(CliErrorCodes.InvalidArguments, "Only one target selector can be used at a time.");
	}

	public TargetSelector ToTargetSelector() =>
		new()
		{
			ProcessId = Pid,
			ProcessName = ProcessName,
			WindowTitle = WindowTitle,
		};

	public CliAttachOptions ToAttachOptions() =>
		new()
		{
			TimeoutMs = TimeoutMs,
			Debug = Debug,
			NoInject = NoInject,
			PipeId = PipeId,
		};

	private static string? SplitOptionValue(string token, out string optionName)
	{
		var equals = token.IndexOf('=');
		if (equals <= 0)
		{
			optionName = token;
			return null;
		}

		optionName = token[..equals];
		return token[(equals + 1)..];
	}

	private static string ReadValue(IReadOnlyList<string> args, ref int index, string option, string? inlineValue)
	{
		if (inlineValue is not null)
			return inlineValue;

		if (index + 1 >= args.Count)
			throw new CliException(CliErrorCodes.InvalidArguments, $"Missing value for '{option}'.");

		var value = args[++index];
		if (value.StartsWith("--", StringComparison.Ordinal))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Missing value for '{option}'.");

		return value;
	}

	private static int ParseInt(string value, string option)
	{
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Invalid integer value for '{option}'.");

		return result;
	}
}
