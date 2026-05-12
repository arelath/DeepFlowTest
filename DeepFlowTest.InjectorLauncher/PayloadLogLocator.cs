namespace DeepFlowTest.InjectorLauncher;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal static class PayloadLogLocator
{
	private const string StartupPrefix = "dft:";
	private const string StartupFilePrefix = "dftfile:";

	public static string GetLogPath(string pipeName, int processId)
	{
		var safePipeName = new string((pipeName ?? "startup").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
		if (string.IsNullOrWhiteSpace(safePipeName))
			safePipeName = "startup";

		return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"DeepFlowTest",
			"payload-logs",
			$"{safePipeName}-{processId}.log");
	}

	public static bool TryReadTail(string startupArgument, int processId, out string tail, int maxCharacters = 4096)
	{
		tail = string.Empty;
		var pipeName = TryExtractPipeName(startupArgument, out var extractedPipeName)
			? extractedPipeName
			: "startup";

		var logPath = GetLogPath(pipeName, processId);
		if (!File.Exists(logPath))
			return false;

		var text = File.ReadAllText(logPath);
		tail = text.Length <= maxCharacters ? text : text.Substring(text.Length - maxCharacters);
		return tail.Length != 0;
	}

	public static bool TryExtractPipeName(string startupArgument, out string pipeName)
	{
		pipeName = string.Empty;
		if (string.IsNullOrWhiteSpace(startupArgument))
			return false;

		string json;
		if (startupArgument.StartsWith(StartupFilePrefix, StringComparison.Ordinal))
		{
			var path = startupArgument.Substring(StartupFilePrefix.Length);
			if (!File.Exists(path))
				return false;

			json = File.ReadAllText(path);
		}
		else if (startupArgument.StartsWith(StartupPrefix, StringComparison.Ordinal))
		{
			var encoded = startupArgument.Substring(StartupPrefix.Length)
				.Replace('-', '+')
				.Replace('_', '/');
			switch (encoded.Length % 4)
			{
				case 2:
					encoded += "==";
					break;
				case 3:
					encoded += "=";
					break;
			}

			try
			{
				json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
			}
			catch (FormatException)
			{
				return false;
			}
		}
		else
		{
			return false;
		}

		var match = Regex.Match(json, @"""(?:pipeName|PipeName)""\s*:\s*""(?<pipe>(?:\\.|[^""\\])*)""");
		if (!match.Success)
			return false;

		pipeName = Regex.Unescape(match.Groups["pipe"].Value);
		return !string.IsNullOrWhiteSpace(pipeName);
	}
}
