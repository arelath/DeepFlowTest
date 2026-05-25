namespace DeepFlowTest;

using System;
using System.Diagnostics;
using DeepFlowTest.Contracts;

public sealed class VirtualPointerOptions
{
	internal const int DefaultHideDelayMs = 800;

	public bool Enabled { get; set; }

	public bool ShowClickRipples { get; set; } = true;

	public bool ShowDragTrail { get; set; } = true;

	public int HideDelayMs { get; set; } = DefaultHideDelayMs;

	public bool IncludeInScreenshots { get; set; }

	internal bool IsDefault =>
		!Enabled
		&& ShowClickRipples
		&& ShowDragTrail
		&& HideDelayMs == DefaultHideDelayMs
		&& !IncludeInScreenshots;
}

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

	public bool AutoSemanticRecordingEnabled { get; set; } = true;

	public string? AutoSemanticRecordingOutputPath { get; set; }

	public SemanticRecordingOptions AutoSemanticRecordingOptions { get; } = new();

	public VirtualPointerOptions VirtualPointer { get; } = new();

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
