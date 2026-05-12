namespace DeepFlowTest.Cli.Tests;

using System;
using NUnit.Framework;

[TestFixture]
public sealed class TargetResolverTests
{
	[Test]
	public void ResolvesByPid()
	{
		var source = Source(Process(42, "App"));
		var resolver = new TargetResolver(source);

		var target = resolver.Resolve(new TargetSelector { ProcessId = 42 });

		Assert.That(target.ProcessName, Is.EqualTo("App"));
	}

	[Test]
	public void MissingPidMapsToTargetNotFound()
	{
		var resolver = new TargetResolver(Source());

		var ex = Assert.Throws<CliException>(() => resolver.Resolve(new TargetSelector { ProcessId = 42 }));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.TargetNotFound));
	}

	[Test]
	public void ResolvesProcessNameExactBeforeContains()
	{
		var resolver = new TargetResolver(Source(Process(1, "App"), Process(2, "AppHelper")));

		var target = resolver.Resolve(new TargetSelector { ProcessName = "App" });

		Assert.That(target.ProcessId, Is.EqualTo(1));
	}

	[Test]
	public void ResolvesProcessNameContains()
	{
		var resolver = new TargetResolver(Source(Process(2, "AppHelper")));

		var target = resolver.Resolve(new TargetSelector { ProcessName = "Helper" });

		Assert.That(target.ProcessId, Is.EqualTo(2));
	}

	[Test]
	public void ProcessNameCacheCanResolveBeforeLiveScan()
	{
		var cache = new ProcessNameCache();
		cache.Remember(new[] { Process(7, "CachedApp") });
		var source = Source();
		var resolver = new TargetResolver(source, cache);

		var target = resolver.Resolve(new TargetSelector { ProcessName = "CachedApp" });

		Assert.That(target.ProcessId, Is.EqualTo(7));
		Assert.That(source.CallCount, Is.EqualTo(0));
	}

	[Test]
	public void AmbiguousProcessNameIncludesCandidates()
	{
		var resolver = new TargetResolver(Source(Process(1, "AppOne"), Process(2, "AppTwo")));

		var ex = Assert.Throws<CliException>(() => resolver.Resolve(new TargetSelector { ProcessName = "App" }));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.AmbiguousTarget));
		Assert.That(ex.Details, Is.Not.Null);
	}

	[Test]
	public void ResolvesWindowTitleSubstring()
	{
		var resolver = new TargetResolver(Source(Process(5, "App", "Main Window")));

		var target = resolver.Resolve(new TargetSelector { WindowTitle = "main" });

		Assert.That(target.ProcessId, Is.EqualTo(5));
	}

	[Test]
	public void ExitedProcessMapsToTargetExited()
	{
		var resolver = new TargetResolver(Source(Process(5, "App", hasExited: true)));

		var ex = Assert.Throws<CliException>(() => resolver.Resolve(new TargetSelector { ProcessId = 5 }));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.TargetExited));
	}

	private static FakeProcessSnapshotSource Source(params ProcessSnapshot[] processes) =>
		new()
		{
			Result = new ProcessSnapshotResult
			{
				Processes = processes,
				Warnings = Array.Empty<ProcessInspectionWarning>(),
			},
		};

	private static ProcessSnapshot Process(int pid, string name, string? title = null, bool hasExited = false) =>
		new()
		{
			ProcessId = pid,
			ProcessName = name,
			MainWindowTitle = title,
			HasExited = hasExited,
			TargetProcess = new FakeTargetProcess { Id = pid, ProcessName = name, HasExited = hasExited },
		};
}
