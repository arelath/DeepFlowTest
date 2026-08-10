namespace DeepFlowTest;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using DeepFlowTest.Contracts;

internal static class PayloadDiagnosticsPaths
{
	private const int DefaultCrashLogMaxCharacters = 8192;

	public static string GetPayloadLogPath(string pipeName, int processId)
	{
		var safePipeName = new string((pipeName ?? "startup").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
		if (string.IsNullOrWhiteSpace(safePipeName))
			safePipeName = "startup";

		return Path.Combine(DefaultLogDirectory, $"{safePipeName}-{processId}.log");
	}

	public static bool TryReadCrashLog(string pipeName, out string crashLog, int maxCharacters = DefaultCrashLogMaxCharacters)
	{
		crashLog = string.Empty;
		if (string.IsNullOrWhiteSpace(pipeName))
			return false;

		var path = Path.Combine(DefaultLogDirectory, $"{SanitizeFileName(pipeName)}-crash.txt");
		var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutDefaults.PayloadCrashLogWaitMs);
		do
		{
			try
			{
				if (File.Exists(path))
				{
					var text = File.ReadAllText(path);
					crashLog = text.Length <= maxCharacters ? text : text.Substring(text.Length - maxCharacters);
					return !string.IsNullOrWhiteSpace(crashLog);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}

			if (DateTime.UtcNow >= deadline)
				return false;

			Thread.Sleep(TimeoutDefaults.PayloadCrashLogPollDelayMs);
		}
		while (true);
	}

	public static bool TryReadPayloadLogTail(string pipeName, int processId, out string tail, int maxCharacters = 4096)
	{
		tail = string.Empty;
		try
		{
			var path = GetPayloadLogPath(pipeName, processId);
			if (!File.Exists(path))
				return false;

			var text = File.ReadAllText(path);
			tail = text.Length <= maxCharacters ? text : text.Substring(text.Length - maxCharacters);
			return tail.Length != 0;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static string DefaultLogDirectory =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepFlowTest", "payload-logs");

	private static string SanitizeFileName(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
	}
}
