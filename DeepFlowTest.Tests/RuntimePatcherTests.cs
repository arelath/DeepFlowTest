namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Patching;
using NUnit.Framework;

[TestFixture]
public sealed class RuntimePatcherTests
{
	[SetUp]
	public void Reset()
	{
		AppDriverPayload.ResetRuntimeForTests();
	}

	[TestCase(RuntimeFrameworkFamilies.NetFramework)]
	[TestCase(RuntimeFrameworkFamilies.NetCore)]
	[TestCase(RuntimeFrameworkFamilies.ModernNet)]
	public void CoordinatorSelectsPatcherForRuntimeFamily(string frameworkFamily)
	{
		var coordinator = new RuntimeWpfPatchCoordinator(
			new FakeFrameworkDetector(frameworkFamily),
			new IWpfPatcher[]
			{
				new NamedPatcher(RuntimeFrameworkFamilies.NetFramework),
				new NamedPatcher(RuntimeFrameworkFamilies.NetCore),
				new NamedPatcher(RuntimeFrameworkFamilies.ModernNet),
			});

		Assert.That(coordinator.SelectPatcher()!.FrameworkFamily, Is.EqualTo(frameworkFamily));
	}

	[Test]
	public void FailedOptionalPatchDoesNotPreventOtherPatches()
	{
		var logMessages = new List<string>();
		var patcher = new TestOptionalPatcher(RuntimeFrameworkFamilies.ModernNet, new[]
		{
			new OptionalWpfPatch("applied", () => true, () => { }),
			new OptionalWpfPatch("skipped", () => false, () => { }),
			new OptionalWpfPatch("failed", () => true, () => throw new InvalidOperationException("missing member")),
			new OptionalWpfPatch("applied-after-failure", () => true, () => { }),
		});
		var coordinator = new RuntimeWpfPatchCoordinator(new FakeFrameworkDetector(RuntimeFrameworkFamilies.ModernNet), new[] { patcher });

		var result = coordinator.ApplyCurrentRuntime((message, _) => logMessages.Add(message));

		Assert.That(result.AppliedPatchNames, Is.EqualTo(new[] { "applied", "applied-after-failure" }));
		Assert.That(result.SkippedPatchNames, Is.EqualTo(new[] { "skipped" }));
		Assert.That(result.FailedPatchNames, Is.EqualTo(new[] { "failed" }));
		Assert.That(result.HasFailures, Is.True);
		Assert.That(logMessages.Any(static message => message.Contains("continuing startup")), Is.True);
	}

	[Test]
	public void AppHooksRecordsDiagnosticsFromCoordinator()
	{
		var coordinator = new RuntimeWpfPatchCoordinator(
			new FakeFrameworkDetector(RuntimeFrameworkFamilies.NetCore),
			new[] { new TestOptionalPatcher(RuntimeFrameworkFamilies.NetCore, new[] { new OptionalWpfPatch("patch", () => true, () => { }) }) });

		var result = AppHooks.Apply(coordinator: coordinator);

		Assert.That(result.AppliedPatchNames, Is.EqualTo(new[] { "patch" }));
		Assert.That(AppHooks.LastResult.FrameworkFamily, Is.EqualTo(RuntimeFrameworkFamilies.NetCore));
		Assert.That(AppHooks.LastResult.AppliedPatchNames, Is.EqualTo(new[] { "patch" }));
	}

	private sealed class FakeFrameworkDetector : IRuntimeFrameworkDetector
	{
		private readonly string frameworkFamily;

		public FakeFrameworkDetector(string frameworkFamily)
		{
			this.frameworkFamily = frameworkFamily;
		}

		public string GetFrameworkFamily() => frameworkFamily;
	}

	private sealed class NamedPatcher : IWpfPatcher
	{
		public NamedPatcher(string frameworkFamily)
		{
			FrameworkFamily = frameworkFamily;
		}

		public string FrameworkFamily { get; }

		public WpfPatchResult Apply(Action<string, Exception?>? log = null) =>
			new() { FrameworkFamily = FrameworkFamily };
	}

	private sealed class TestOptionalPatcher : WpfPatcherBase
	{
		public TestOptionalPatcher(string frameworkFamily, IEnumerable<OptionalWpfPatch> patches)
			: base(frameworkFamily, patches)
		{
		}
	}
}
