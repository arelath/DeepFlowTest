namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DeepFlowTest.Contracts;

public sealed class VirtualPointerOptions
{
	internal static readonly TimeSpan DefaultHideDelay = TimeSpan.FromMilliseconds(800);

	public bool Enabled { get; init; }

	public bool ShowClickRipples { get; init; } = true;

	public bool ShowDragTrail { get; init; } = true;

	public TimeSpan HideDelay { get; init; } = DefaultHideDelay;

	public bool IncludeInScreenshots { get; init; }

	internal bool IsDefault =>
		!Enabled
		&& ShowClickRipples
		&& ShowDragTrail
		&& HideDelay == DefaultHideDelay
		&& !IncludeInScreenshots;

	internal void Validate()
	{
		_ = DurationUtility.ToMilliseconds(HideDelay, nameof(HideDelay), allowZero: true);
	}
}

public class AppDriverOptions
{
	private IReadOnlyList<TimeSpan> elementPollBackoff = CreateDefaultElementPollBackoff();
	private long timeoutTicks = TimeoutDefaults.AppDriverTimeout.Ticks;

	public TimeSpan Timeout
	{
		get => TimeSpan.FromTicks(Interlocked.Read(ref timeoutTicks));
		set
		{
			_ = DurationUtility.ToMilliseconds(value, nameof(Timeout));
			Interlocked.Exchange(ref timeoutTicks, value.Ticks);
		}
	}

	public bool AllowInjection { get; init; } = true;

	public string? PipeName { get; init; }

	public string PayloadRoot { get; init; } = AppContext.BaseDirectory;

	public string InjectorLauncherPath { get; init; } = ResolveDefaultInjectorLauncherPath();

	public IReadOnlyList<TimeSpan> ElementPollBackoff
	{
		get => elementPollBackoff;
		init => elementPollBackoff = Copy(value, nameof(ElementPollBackoff));
	}

	public bool FailOnBindingFailures { get; init; }

	public BindingFailureOptions BindingFailures { get; init; } = new();

	public AutomaticDiagnosticsOptions AutomaticDiagnostics { get; init; } = new();

	public bool AutoSemanticRecordingEnabled { get; init; }

	public string? AutoSemanticRecordingOutputPath { get; init; }

	public SemanticRecordingOptions AutoSemanticRecordingOptions { get; init; } = new();

	public VirtualPointerOptions VirtualPointer { get; init; } = new();

	internal void Validate()
	{
		_ = DurationUtility.ToMilliseconds(Timeout, nameof(Timeout));
		if (string.IsNullOrWhiteSpace(PayloadRoot))
			throw new ArgumentException("A payload root is required.", nameof(PayloadRoot));
		if (string.IsNullOrWhiteSpace(InjectorLauncherPath))
			throw new ArgumentException("An injector launcher path is required.", nameof(InjectorLauncherPath));
		if (elementPollBackoff is null)
			throw new ArgumentNullException(nameof(ElementPollBackoff));
		foreach (var delay in elementPollBackoff)
			_ = DurationUtility.ToMilliseconds(delay, nameof(ElementPollBackoff), allowZero: true);

		(BindingFailures ?? throw new ArgumentNullException(nameof(BindingFailures))).Validate();
		(AutomaticDiagnostics ?? throw new ArgumentNullException(nameof(AutomaticDiagnostics))).Validate();
		(AutoSemanticRecordingOptions ?? throw new ArgumentNullException(nameof(AutoSemanticRecordingOptions))).Validate();
		(VirtualPointer ?? throw new ArgumentNullException(nameof(VirtualPointer))).Validate();
	}

	private static IReadOnlyList<TimeSpan> CreateDefaultElementPollBackoff() =>
		new ReadOnlyCollection<TimeSpan>(TimeoutDefaults.CreateElementPollBackoffMs().Select(static milliseconds => TimeSpan.FromMilliseconds(milliseconds)).ToArray());

	private static IReadOnlyList<TimeSpan> Copy(IReadOnlyList<TimeSpan>? source, string parameterName) =>
		new ReadOnlyCollection<TimeSpan>((source ?? throw new ArgumentNullException(parameterName)).ToArray());

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
	public string? Arguments { get; init; }

	public string? WorkingDirectory { get; init; }

	public ProcessStartInfo? ProcessStartInfo { get; init; }

	public bool OwnsProcess { get; init; } = true;
}

public sealed class AppDriverAttachOptions : AppDriverOptions
{
	public bool AllowContainsProcessNameMatch { get; init; } = true;
}
