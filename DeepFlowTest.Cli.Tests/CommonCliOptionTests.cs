namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class CommonCliOptionTests
{
	[Test]
	public void MissingTargetSelectorProducesInvalidArguments()
	{
		var result = CliTestHost.Run(new[] { "ping" });

		Assert.That(result.ExitCode, Is.EqualTo(1));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"invalid-arguments\""));
	}

	[Test]
	public void MultipleTargetSelectorsProduceInvalidArguments()
	{
		var result = CliTestHost.Run(new[] { "ping", "--pid", "1", "--process", "app" });

		Assert.That(result.ExitCode, Is.EqualTo(1));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"invalid-arguments\""));
	}

	[Test]
	public void InvalidFormatProducesInvalidArguments()
	{
		var result = CliTestHost.Run(new[] { "processes", "--format", "yaml" });

		Assert.That(result.ExitCode, Is.EqualTo(1));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"invalid-arguments\""));
	}

	[Test]
	public void InvalidAfterModeProducesInvalidArguments()
	{
		var result = CliTestHost.Run(new[] { "ping", "--pid", "1", "--after", "everything" });

		Assert.That(result.ExitCode, Is.EqualTo(1));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"invalid-arguments\""));
	}

	[Test]
	public void CommandLineOptionsOverrideDefaults()
	{
		var defaults = new CliDefaults { OutputFormat = "json", TimeoutMs = 10_000 };
		var parsed = CliCommonOptions.Parse(new[] { "ping", "--pid", "5", "--format", "text", "--timeout-ms", "25" }, defaults);

		Assert.That(parsed.Format, Is.EqualTo("text"));
		Assert.That(parsed.TimeoutMs, Is.EqualTo(25));
	}
}
