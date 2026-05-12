namespace DeepFlowTest;

using System;

public class AppDriverOptions
{
	public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

	public bool AllowInjection { get; set; } = true;

	public string? PipeName { get; set; }

	public string PayloadRoot { get; set; } = AppContext.BaseDirectory;

	public string InjectorLauncherPath { get; set; } = System.IO.Path.Combine(AppContext.BaseDirectory, "DeepFlowTest.InjectorLauncher.exe");

	public int[] ElementPollBackoffMs { get; set; } = { 25, 100, 500, 1000, 2000 };
}

public sealed class AppDriverLaunchOptions : AppDriverOptions
{
	public string? Arguments { get; set; }

	public string? WorkingDirectory { get; set; }

	public bool OwnsProcess { get; set; } = true;
}

public sealed class AppDriverAttachOptions : AppDriverOptions
{
	public bool AllowContainsProcessNameMatch { get; set; } = true;
}
