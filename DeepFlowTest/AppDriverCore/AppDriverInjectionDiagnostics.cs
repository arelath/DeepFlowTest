namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.IO;

internal static class AppDriverInjectionDiagnostics
{
	public static string DefaultInjectorLogPath =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"DeepFlowTest",
			"logs",
			"deepflowtest-injector.log");

	public static string? TryReadStartupLogTail(
		string pipeName,
		int processId,
		DateTimeOffset? injectorLogNotBefore = null,
		int maxCharacters = 4096)
	{
		var sections = new List<string>();
		var payloadLogPath = PayloadDiagnosticsPaths.GetPayloadLogPath(pipeName, processId);
		if (TryReadFileTail(payloadLogPath, out var payloadTail, maxCharacters))
			sections.Add(FormatSection("Payload log tail", payloadLogPath, payloadTail));

		if (TryReadFileTail(DefaultInjectorLogPath, out var injectorTail, maxCharacters, injectorLogNotBefore))
			sections.Add(FormatSection("Injector log tail", DefaultInjectorLogPath, injectorTail));

		return sections.Count == 0 ? null : string.Join($"{Environment.NewLine}{Environment.NewLine}", sections);
	}

	public static string AppendDiagnostics(string message, string? diagnostics)
	{
		if (string.IsNullOrWhiteSpace(diagnostics))
			return message;

		return $"{message}{Environment.NewLine}{Environment.NewLine}Injection diagnostics:{Environment.NewLine}{diagnostics}";
	}

	private static bool TryReadFileTail(
		string path,
		out string tail,
		int maxCharacters,
		DateTimeOffset? notBefore = null)
	{
		tail = string.Empty;
		try
		{
			if (!File.Exists(path))
				return false;

			if (notBefore.HasValue)
			{
				var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
				if (lastWrite < notBefore.Value.ToUniversalTime())
					return false;
			}

			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var reader = new StreamReader(stream);
			var text = reader.ReadToEnd();
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

	private static string FormatSection(string title, string path, string tail) =>
		$"{title} ({path}):{Environment.NewLine}{tail.TrimEnd()}";
}
