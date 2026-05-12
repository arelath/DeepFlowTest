namespace DeepFlowTest.Cli.Tests;

using System;
using NUnit.Framework;

[TestFixture]
public sealed class ProgramRunTests
{
	[Test]
	public void InvalidArgumentsMapToEnvelopeAndExitCodeOne()
	{
		var result = CliTestHost.Run(new[] { "unknown-command" });

		Assert.That(result.ExitCode, Is.EqualTo(1));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"invalid-arguments\""));
	}

	[Test]
	public void UnexpectedExceptionMapsToExitCodeNine()
	{
		var services = CliTestHost.CreateServices(snapshotSource: new ThrowingSnapshotSourceForNegativeTests());

		var result = CliTestHost.Run(new[] { "processes" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(9));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"unexpected-error\""));
	}

	internal sealed class ThrowingSnapshotSourceForNegativeTests : IProcessSnapshotSource
	{
		public ProcessSnapshotResult GetSnapshots()
		{
			throw new InvalidOperationException("boom");
		}
	}
}
