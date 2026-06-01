namespace DeepFlowTest.Cli.Tests;

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

[TestFixture]
public sealed class ProcessesCommandTests
{
	[Test]
	public void ProcessesReturnsSortedJsonShape()
	{
		var source = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes = new[]
				{
					Process(2, "Beta"),
					Process(1, "Alpha"),
				},
				Warnings = Array.Empty<ProcessInspectionWarning>(),
			},
		};
		var services = CliTestHost.CreateServices(snapshotSource: source);

		var result = CliTestHost.Run(new[] { "processes" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout.IndexOf("Alpha", StringComparison.Ordinal), Is.LessThan(result.Stdout.IndexOf("Beta", StringComparison.Ordinal)));
		Assert.That(result.Stdout, Does.Contain("\"processId\":1"));
	}

	[Test]
	public void CandidateFilterKeepsLikelyWpfProcesses()
	{
		var source = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes = new[]
				{
					Process(1, "Plain", isWpf: false),
					Process(2, "WpfApp", isWpf: true),
				},
				Warnings = Array.Empty<ProcessInspectionWarning>(),
			},
		};
		var services = CliTestHost.CreateServices(snapshotSource: source);

		var result = CliTestHost.Run(new[] { "processes", "--candidates-only" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("WpfApp"));
		Assert.That(result.Stdout, Does.Not.Contain("Plain"));
	}

	[Test]
	public void CandidateFilterKeepsLikelyWinFormsProcesses()
	{
		var source = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes = new[]
				{
					Process(1, "NativeWindow", isWpf: false),
					Process(2, "WinFormsApp", isWpf: true, frameworkFamily: "winforms"),
				},
				Warnings = Array.Empty<ProcessInspectionWarning>(),
			},
		};
		var services = CliTestHost.CreateServices(snapshotSource: source);

		var result = CliTestHost.Run(new[] { "processes", "--candidates-only" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("WinFormsApp"));
		Assert.That(result.Stdout, Does.Not.Contain("NativeWindow"));
	}

	[Test]
	public void CandidateFilterOmitsWarnedProcessesAndWarnings()
	{
		var source = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes = new[]
				{
					Process(1, "CleanUi", isWpf: true),
					Process(2, "DeniedUi", isWpf: true),
					Process(3, "Plain", isWpf: false),
					Process(4, "WindowlessWpfBackground", isWpf: true, hasWindow: false),
				},
				Warnings = new[]
				{
					new ProcessInspectionWarning { ProcessId = 2, ProcessName = "DeniedUi", Message = "Access is denied." },
					new ProcessInspectionWarning { ProcessId = 5, ProcessName = "System", Message = "Access is denied." },
				},
			},
		};
		var services = CliTestHost.CreateServices(snapshotSource: source);

		var result = CliTestHost.Run(new[] { "processes", "--candidates-only" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("CleanUi"));
		Assert.That(result.Stdout, Does.Not.Contain("DeniedUi"));
		Assert.That(result.Stdout, Does.Not.Contain("Access is denied."));
	}

	[Test]
	public void CandidateFilterOmitsWindowlessWpfProcesses()
	{
		var source = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes = new[]
				{
					Process(1, "VisibleWpf", isWpf: true),
					Process(2, "PowerToys.PowerLauncher", isWpf: true, hasWindow: false),
				},
				Warnings = Array.Empty<ProcessInspectionWarning>(),
			},
		};
		var services = CliTestHost.CreateServices(snapshotSource: source);

		var result = CliTestHost.Run(new[] { "processes", "--candidates-only" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("VisibleWpf"));
		Assert.That(result.Stdout, Does.Not.Contain("PowerToys.PowerLauncher"));
	}

	[Test]
	public void ShowAllKeepsWindowlessWpfProcessesForManualAttach()
	{
		var source = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes = new[]
				{
					Process(1, "VisibleWpf", isWpf: true),
					Process(2, "PowerToys.PowerLauncher", isWpf: true, hasWindow: false),
				},
				Warnings = Array.Empty<ProcessInspectionWarning>(),
			},
		};
		var services = CliTestHost.CreateServices(snapshotSource: source);

		var result = CliTestHost.Run(new[] { "processes", "--show-all" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("VisibleWpf"));
		Assert.That(result.Stdout, Does.Contain("PowerToys.PowerLauncher"));
	}

	[Test]
	public void ShowAllCompatibilityOptionKeepsAllProcesses()
	{
		var source = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes = new[]
				{
					Process(1, "Plain", isWpf: false),
					Process(2, "WpfApp", isWpf: true),
				},
				Warnings = Array.Empty<ProcessInspectionWarning>(),
			},
		};
		var services = CliTestHost.CreateServices(snapshotSource: source);

		var result = CliTestHost.Run(new[] { "processes", "--show-all" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("Plain"));
		Assert.That(result.Stdout, Does.Contain("WpfApp"));
	}

	[Test]
	public void ShowAllCompatibilityOptionOverridesCandidateFilter()
	{
		var source = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes = new[]
				{
					Process(1, "Plain", isWpf: false),
					Process(2, "WpfApp", isWpf: true),
				},
				Warnings = Array.Empty<ProcessInspectionWarning>(),
			},
		};
		var services = CliTestHost.CreateServices(snapshotSource: source);

		var result = CliTestHost.Run(new[] { "processes", "--candidates-only", "--show-all" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("Plain"));
		Assert.That(result.Stdout, Does.Contain("WpfApp"));
	}

	[Test]
	public void InaccessibleProcessWarningDoesNotFailCommand()
	{
		var source = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes = Array.Empty<ProcessSnapshot>(),
				Warnings = new[]
				{
					new ProcessInspectionWarning { ProcessId = 99, ProcessName = "Denied", Message = "access denied" },
				},
			},
		};
		var services = CliTestHost.CreateServices(snapshotSource: source);

		var result = CliTestHost.Run(new[] { "processes" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("access denied"));
	}

	[Test]
	public void TextOutputIsReadable()
	{
		var source = new FakeProcessSnapshotSource
		{
			Result = new ProcessSnapshotResult
			{
				Processes = new[] { Process(1, "Alpha") },
				Warnings = Array.Empty<ProcessInspectionWarning>(),
			},
		};
		var services = CliTestHost.CreateServices(snapshotSource: source);

		var result = CliTestHost.Run(new[] { "processes", "--format", "text" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("PID"));
		Assert.That(result.Stdout, Does.Contain("Alpha"));
	}

	[Test]
	public void LiveProcessSnapshotIncludesCurrentProcessArchitectureAndDoesNotRetainHandle()
	{
		var result = new LiveProcessSnapshotSource().GetSnapshots();

		var current = result.Processes.SingleOrDefault(process => process.ProcessId == Environment.ProcessId);

		Assert.That(current, Is.Not.Null);
		Assert.That(current!.Architecture, Is.Not.Null.And.Not.Empty);
		Assert.That(current.TargetProcess, Is.Null);
	}

	[TestCase("PresentationFramework.dll", true)]
	[TestCase("PresentationFramework.ni.dll", true)]
	[TestCase("PresentationCore.ni.dll", true)]
	[TestCase("System.Windows.Forms.dll", false)]
	public void WpfModuleDetectionRecognizesFrameworkNativeImages(string moduleName, bool expected)
	{
		var method = typeof(LiveProcessSnapshotSource).GetMethod("IsWpfModule", BindingFlags.NonPublic | BindingFlags.Static);

		Assert.That(method, Is.Not.Null);
		Assert.That((bool)method!.Invoke(null, new object[] { moduleName })!, Is.EqualTo(expected));
	}

	[TestCase("System.Windows.Forms.dll", true)]
	[TestCase("System.Windows.Forms.ni.dll", true)]
	[TestCase("PresentationFramework.ni.dll", false)]
	public void WinFormsModuleDetectionRecognizesFrameworkNativeImages(string moduleName, bool expected)
	{
		var method = typeof(LiveProcessSnapshotSource).GetMethod("IsWinFormsModule", BindingFlags.NonPublic | BindingFlags.Static);

		Assert.That(method, Is.Not.Null);
		Assert.That((bool)method!.Invoke(null, new object[] { moduleName })!, Is.EqualTo(expected));
	}

	private static ProcessSnapshot Process(int pid, string name, bool isWpf = false, bool hasWindow = true, string? frameworkFamily = null) =>
		new()
		{
			ProcessId = pid,
			ProcessName = name,
			MainWindowTitle = hasWindow ? name : string.Empty,
			TopLevelWindows = hasWindow ? [new ProcessWindowSnapshot { Hwnd = pid, Title = name }] : [],
			FrameworkFamily = frameworkFamily ?? (isWpf ? "wpf" : string.Empty),
			IsLikelyWpfCandidate = isWpf,
			TargetProcess = new FakeTargetProcess { Id = pid, ProcessName = name },
		};
}
