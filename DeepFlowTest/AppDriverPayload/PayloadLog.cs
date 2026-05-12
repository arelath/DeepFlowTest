namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

public static class PayloadLog
{
	private static string? activeLogPath;

	public static string DefaultLogDirectory =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepFlowTest", "payload-logs");

	public static string CurrentLogPath => activeLogPath ?? GetLogPath("startup", Process.GetCurrentProcess().Id);

	public static string Initialize(string pipeName, int? processId = null)
	{
		activeLogPath = GetLogPath(pipeName, processId ?? Process.GetCurrentProcess().Id);
		Directory.CreateDirectory(Path.GetDirectoryName(activeLogPath)!);
		Write("Payload logging initialized.");
		return activeLogPath;
	}

	public static string GetLogPath(string pipeName, int processId)
	{
		var safePipeName = new string((pipeName ?? "startup").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
		if (string.IsNullOrWhiteSpace(safePipeName))
			safePipeName = "startup";

		return Path.Combine(DefaultLogDirectory, $"{safePipeName}-{processId}.log");
	}

	public static void Write(string message, Exception? exception = null)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(CurrentLogPath)!);
			File.AppendAllText(CurrentLogPath, $"{DateTimeOffset.Now:O}: {message}{Environment.NewLine}{exception}{Environment.NewLine}");
		}
		catch
		{
			// Logging must never crash the target process.
		}
	}

	public static string ReadTail(string logPath, int maxCharacters = 4096)
	{
		if (!File.Exists(logPath))
			return string.Empty;

		var text = File.ReadAllText(logPath);
		return text.Length <= maxCharacters ? text : text.Substring(text.Length - maxCharacters);
	}

	public static bool TryReadTailForPipe(string pipeName, int processId, out string tail, int maxCharacters = 4096)
	{
		var logPath = GetLogPath(pipeName, processId);
		tail = ReadTail(logPath, maxCharacters);
		return tail.Length != 0;
	}

	public static void ResetForTests()
	{
		activeLogPath = null;
	}
}
