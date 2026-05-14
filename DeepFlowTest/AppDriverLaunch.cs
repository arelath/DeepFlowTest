namespace DeepFlowTest;

using System;
using System.Diagnostics;
using System.IO;

internal static class AppDriverLaunch
{
	public static string NormalizeExecutablePath(string executablePath)
	{
		_ = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
		if (string.IsNullOrWhiteSpace(executablePath))
			throw new ArgumentException("Executable path is required.", nameof(executablePath));

		return Path.GetFullPath(Environment.ExpandEnvironmentVariables(executablePath));
	}

	public static ProcessStartInfo ResolveStartInfo(string executablePath, AppDriverLaunchOptions options)
	{
		_ = options ?? throw new ArgumentNullException(nameof(options));
		if (options.ProcessStartInfo is not null)
			return options.ProcessStartInfo;

		var startInfo = new ProcessStartInfo(NormalizeExecutablePath(executablePath), options.Arguments ?? string.Empty);
		if (!string.IsNullOrWhiteSpace(options.WorkingDirectory))
			startInfo.WorkingDirectory = options.WorkingDirectory;

		return startInfo;
	}
}
