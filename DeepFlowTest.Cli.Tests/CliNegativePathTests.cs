namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class CliNegativePathTests
{
	[TestCase(new[] { "unknown-command" }, 1, "invalid-arguments")]
	[TestCase(new[] { "ping" }, 1, "invalid-arguments")]
	[TestCase(new[] { "find", "--pid", "1234", "--property-regex", "Text=[" }, 1, "invalid-arguments")]
	[TestCase(new[] { "find", "--pid", "1234", "--name", "missing", "--require-match" }, 8, "no-match")]
	[TestCase(new[] { "node", "--pid", "1234", "--target", "missing" }, 8, "stale-target")]
	[TestCase(new[] { "stream", "visual-tree", "--pid", "1234", "--interval-ms", "1" }, 1, "invalid-arguments")]
	[TestCase(new[] { "invoke", "--pid", "1234", "--target", "0002", "--code", "\"Method\"" }, 1, "arbitrary-invoke-denied")]
	public void CliErrorsUseStableEnvelopeAndExitCode(string[] args, int exitCode, string errorCode)
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(args, services);

		Assert.That(result.ExitCode, Is.EqualTo(exitCode));
		Assert.That(result.Stdout, Does.Contain($"\"code\":\"{errorCode}\""));
		Assert.That(result.Stdout, Does.Not.Contain("StackTrace"));
	}

	[Test]
	public void UnexpectedErrorsUseExitCodeNine()
	{
		var services = CliTestHost.CreateServices(snapshotSource: new ProgramRunTests.ThrowingSnapshotSourceForNegativeTests());

		var result = CliTestHost.Run(new[] { "processes" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(9));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"unexpected-error\""));
	}
}
