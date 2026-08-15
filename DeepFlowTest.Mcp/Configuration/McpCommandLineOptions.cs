namespace DeepFlowTest.Mcp.Configuration;

using System;
using System.Collections.Generic;
using DeepFlowTest.Automation;

internal static class McpCommandLineOptions
{
	public static void Apply(McpServerOptions options, IReadOnlyList<string> args)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(args);

		for (var i = 0; i < args.Count; i++)
		{
			var token = args[i];
			var inlineValue = SplitOptionValue(token, out var optionName);
			switch (optionName)
			{
				case "--pid":
					options.Startup.ProcessId = ParseInt(ReadValue(args, ref i, optionName, inlineValue), optionName);
					break;
				case "--process":
					options.Startup.ProcessName = ReadValue(args, ref i, optionName, inlineValue);
					break;
				case "--window-title":
					options.Startup.WindowTitle = ReadValue(args, ref i, optionName, inlineValue);
					break;
				case "--launch":
					options.Startup.LaunchPath = ReadValue(args, ref i, optionName, inlineValue);
					break;
				case "--launch-args":
					options.Startup.LaunchArguments = ReadValue(args, ref i, optionName, inlineValue, allowOptionLikeValue: true);
					break;
				case "--working-dir":
				case "--working-directory":
					options.Startup.WorkingDirectory = ReadValue(args, ref i, optionName, inlineValue);
					break;
				case "--terminate-on-detach":
					options.Startup.TerminateOnDetach = true;
					break;
				case "--timeout-ms":
					var timeoutMs = ParseInt(ReadValue(args, ref i, optionName, inlineValue), optionName);
					options.DefaultTimeoutMs = timeoutMs;
					options.AttachTimeoutMs = timeoutMs;
					break;
				case "--attach-timeout-ms":
					options.AttachTimeoutMs = ParseInt(ReadValue(args, ref i, optionName, inlineValue), optionName);
					break;
				case "--cache-ttl-ms":
					options.CacheTtlMs = ParseInt(ReadValue(args, ref i, optionName, inlineValue), optionName);
					break;
				case "--tree-limit":
					options.TreeLimit = ParseInt(ReadValue(args, ref i, optionName, inlineValue), optionName);
					break;
				case "--stream-buffer-size":
					options.StreamBufferSize = ParseInt(ReadValue(args, ref i, optionName, inlineValue), optionName);
					break;
				case "--resource-retention-limit":
					options.ResourceRetentionLimit = ParseInt(ReadValue(args, ref i, optionName, inlineValue), optionName);
					break;
				case "--activity-retention-limit":
					options.ActivityRetentionLimit = ParseInt(ReadValue(args, ref i, optionName, inlineValue), optionName);
					break;
				case "--activity-log-file":
					options.ActivityLogFile = ReadValue(args, ref i, optionName, inlineValue);
					break;
				case "--context-idle-timeout-ms":
					options.ContextIdleTimeoutMs = ParseInt(ReadValue(args, ref i, optionName, inlineValue), optionName);
					break;
				case "--tool-profile":
					options.ToolProfile = ParseToolProfile(ReadValue(args, ref i, optionName, inlineValue));
					break;
				case "--http-host":
					options.Http.Host = ReadValue(args, ref i, optionName, inlineValue);
					break;
				case "--http-port":
					options.Http.Port = ParseInt(ReadValue(args, ref i, optionName, inlineValue), optionName);
					break;
				case "--http-path":
					options.Http.Path = NormalizeHttpPath(ReadValue(args, ref i, optionName, inlineValue));
					break;
				case "--http-enable-legacy-sse":
					options.Http.EnableLegacySse = true;
					break;
				case "--endpoint-file":
					options.Http.EndpointFile = ReadValue(args, ref i, optionName, inlineValue);
					break;
				case "--start-minimized":
					options.Http.StartMinimized = true;
					break;
				case "--no-inject":
					options.Startup.NoInject = true;
					break;
				case "--pipe-id":
					options.Startup.PipeId = ReadValue(args, ref i, optionName, inlineValue);
					break;
				case "--allow-launch":
					options.Policy.AllowLaunch = true;
					break;
				case "--allow-actions":
					options.Policy.AllowActions = true;
					break;
				case "--allow-arbitrary-invoke":
					options.Policy.AllowArbitraryInvoke = true;
					break;
				case "--allow-file-writes":
					options.Policy.AllowFileWrites = true;
					break;
			}
		}
	}

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

	private static string ReadValue(
		IReadOnlyList<string> args,
		ref int index,
		string option,
		string? inlineValue,
		bool allowOptionLikeValue = false)
	{
		if (inlineValue is not null)
			return inlineValue;

		if (index + 1 >= args.Count)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Missing value for '{option}'.");

		var value = args[++index];
		if (!allowOptionLikeValue && value.StartsWith("--", StringComparison.Ordinal))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Missing value for '{option}'.");

		return value;
	}

	private static string NormalizeHttpPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "HTTP path cannot be empty.");

		path = path.Trim();
		return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
	}

	private static int ParseInt(string value, string option)
	{
		if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var result))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Invalid integer value for '{option}'.");

		return result;
	}

	private static McpToolProfile ParseToolProfile(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"agent" => McpToolProfile.Agent,
			"full" or "legacy" => McpToolProfile.Full,
			_ => throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Invalid tool profile '{value}'. Expected 'agent' or 'full'."),
		};
}
