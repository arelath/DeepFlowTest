namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class SmokeTests
{
	[Test]
	public void ProgramRunReturnsSuccess()
	{
		Assert.That(Program.Run(System.Array.Empty<string>()), Is.EqualTo(0));
	}

	[Test]
	public void ProgramRunRejectsUnknownCommand()
	{
		Assert.That(Program.Run(new[] { "unknown-command" }), Is.EqualTo(2));
	}
}
