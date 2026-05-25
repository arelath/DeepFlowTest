namespace DeepFlowTest.InjectorLauncher;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

internal static class ArchitectureRedirect
{
	public static ProcessStartInfo? CreateStartInfo(
		string currentExecutablePath,
		string currentArchitecture,
		string targetArchitecture,
		string[] originalArgs)
	{
		currentArchitecture = ArchitectureDetector.Normalize(currentArchitecture);
		targetArchitecture = ArchitectureDetector.Normalize(targetArchitecture);
		if (currentArchitecture.Equals(targetArchitecture, StringComparison.Ordinal))
			return null;

		var targetExecutablePath = GetLauncherPath(currentExecutablePath, targetArchitecture);
		if (!File.Exists(targetExecutablePath))
		{
			throw new InjectorLauncherException(
				InjectorExitCode.MissingArchitectureLauncher,
				$"Missing architecture-specific launcher '{targetExecutablePath}'.");
		}

		return new ProcessStartInfo(targetExecutablePath, EscapeArguments(originalArgs))
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = Path.GetDirectoryName(targetExecutablePath) ?? Environment.CurrentDirectory,
		};
	}

	public static int Run(ProcessStartInfo startInfo, Func<ProcessStartInfo, IRedirectedProcess?>? startProcess = null)
	{
		startProcess ??= static info =>
		{
			var process = Process.Start(info);
			return process is null ? null : new RedirectedProcess(process);
		};

		using var redirectedProcess = startProcess(startInfo);
		if (redirectedProcess is null)
			return InjectorExitCode.MissingArchitectureLauncher;

		redirectedProcess.WaitForExit();
		return redirectedProcess.ExitCode;
	}

	public static string GetLauncherPath(string currentExecutablePath, string targetArchitecture)
	{
		var directory = Path.GetDirectoryName(currentExecutablePath) ?? string.Empty;
		var normalizedTargetArchitecture = ArchitectureDetector.Normalize(targetArchitecture);
		var executableName = $"DeepFlowTest.InjectorLauncher.{normalizedTargetArchitecture}.exe";
		var sameDirectoryPath = Path.Combine(directory, executableName);
		if (File.Exists(sameDirectoryPath))
			return sameDirectoryPath;

		var currentArchitectureDirectory = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		var resourceDirectory = Path.GetDirectoryName(directory);
		if (IsArchitectureDirectory(currentArchitectureDirectory) &&
			resourceDirectory is not null &&
			Path.GetFileName(resourceDirectory).Equals(InjectorPathResolver.ResourceFolderName, StringComparison.OrdinalIgnoreCase))
		{
			return Path.Combine(resourceDirectory, normalizedTargetArchitecture, executableName);
		}

		return sameDirectoryPath;
	}

	private static bool IsArchitectureDirectory(string value) =>
		value.Equals(ArchitectureDetector.X86, StringComparison.OrdinalIgnoreCase) ||
		value.Equals(ArchitectureDetector.X64, StringComparison.OrdinalIgnoreCase);

	public static string EscapeArguments(string[] args)
	{
		return string.Join(" ", args.Select(EscapeArgument));
	}

	private static string EscapeArgument(string arg)
	{
		if (arg.Length == 0)
			return "\"\"";

		if (!arg.Any(static c => char.IsWhiteSpace(c) || c == '"'))
			return arg;

		var builder = new StringBuilder();
		builder.Append('"');
		var backslashes = 0;
		foreach (var c in arg)
		{
			if (c == '\\')
			{
				backslashes++;
				continue;
			}

			if (c == '"')
			{
				builder.Append('\\', backslashes * 2 + 1);
				builder.Append('"');
				backslashes = 0;
				continue;
			}

			builder.Append('\\', backslashes);
			builder.Append(c);
			backslashes = 0;
		}

		builder.Append('\\', backslashes * 2);
		builder.Append('"');
		return builder.ToString();
	}

	private sealed class RedirectedProcess : IRedirectedProcess
	{
		private readonly Process process;

		public RedirectedProcess(Process process)
		{
			this.process = process;
		}

		public int ExitCode => process.ExitCode;

		public void WaitForExit()
		{
			process.WaitForExit();
		}

		public void Dispose()
		{
			process.Dispose();
		}
	}
}

internal interface IRedirectedProcess : IDisposable
{
	int ExitCode { get; }

	void WaitForExit();
}
