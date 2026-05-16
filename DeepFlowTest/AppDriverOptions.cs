namespace DeepFlowTest;

using System;
using System.Diagnostics;
using DeepFlowTest.Contracts;

public class AppDriverOptions
{
	public TimeSpan Timeout { get; set; } = TimeoutDefaults.AppDriverTimeout;

	public bool AllowInjection { get; set; } = true;

	public string? PipeName { get; set; }

	public string PayloadRoot { get; set; } = AppContext.BaseDirectory;

	public string InjectorLauncherPath { get; set; } = ResolveDefaultInjectorLauncherPath();

	public int[] ElementPollBackoffMs { get; set; } = TimeoutDefaults.CreateElementPollBackoffMs();

	public bool FailOnBindingFailures { get; set; }

	public BindingFailureOptions BindingFailures { get; } = new();

	private static string ResolveDefaultInjectorLauncherPath()
	{
		var architecture = Environment.Is64BitProcess ? "x64" : "x86";
		var architectureSpecific = System.IO.Path.Combine(
			AppContext.BaseDirectory,
			"DeepFlowTestResources",
			architecture,
			$"DeepFlowTest.InjectorLauncher.{architecture}.exe");
		return System.IO.File.Exists(architectureSpecific)
			? architectureSpecific
			: System.IO.Path.Combine(AppContext.BaseDirectory, "DeepFlowTest.InjectorLauncher.exe");
	}
}

public sealed class AppDriverLaunchOptions : AppDriverOptions
{
	public string? Arguments { get; set; }

	public string? WorkingDirectory { get; set; }

	public ProcessStartInfo? ProcessStartInfo { get; set; }

	public bool OwnsProcess { get; set; } = true;
}

public sealed class AppDriverAttachOptions : AppDriverOptions
{
	public bool AllowContainsProcessNameMatch { get; set; } = true;
}
