namespace DeepFlowTest.Cli.Tests;

using System;
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

	private static ProcessSnapshot Process(int pid, string name, bool isWpf = false) =>
		new()
		{
			ProcessId = pid,
			ProcessName = name,
			IsLikelyWpfCandidate = isWpf,
			TargetProcess = new FakeTargetProcess { Id = pid, ProcessName = name },
		};
}
