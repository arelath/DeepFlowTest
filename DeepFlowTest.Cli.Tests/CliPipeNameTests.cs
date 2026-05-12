namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class CliPipeNameTests
{
	[Test]
	public void DefaultPipeNameIncludesProtocolAndPid()
	{
		Assert.That(CliPipeName.ForTarget(123), Is.EqualTo("deepflowtest-cli-v1-pid-123"));
	}

	[Test]
	public void CustomPipeIdUsesCliPrefix()
	{
		Assert.That(CliPipeName.ForTarget(123, "smoke"), Is.EqualTo("deepflowtest-cli-v1-smoke"));
	}

	[TestCase("")]
	[TestCase("has space")]
	[TestCase("has/slash")]
	[TestCase("has\\backslash")]
	[TestCase("has:colon")]
	public void InvalidPipeIdsAreRejected(string pipeId)
	{
		Assert.That(() => CliPipeName.ForTarget(123, pipeId), Throws.TypeOf<CliException>());
	}

	[Test]
	public void NonPositivePidIsRejected()
	{
		Assert.That(() => CliPipeName.ForTarget(0), Throws.TypeOf<CliException>());
	}
}
