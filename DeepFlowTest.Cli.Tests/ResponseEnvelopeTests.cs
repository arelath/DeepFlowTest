namespace DeepFlowTest.Cli.Tests;

using System.Diagnostics;
using NUnit.Framework;

[TestFixture]
public sealed class ResponseEnvelopeTests
{
	[Test]
	public void SuccessEnvelopeIncludesCoreFields()
	{
		var envelope = CliResponseFactory.Success("version", new ProductVersionData { ProductName = "DeepFlowTest" }, Stopwatch.StartNew());
		var json = CliOutput.ToJson(envelope, pretty: false, hideEmpty: true);

		Assert.That(json, Does.Contain("\"ok\":true"));
		Assert.That(json, Does.Contain("\"command\":\"version\""));
		Assert.That(json, Does.Contain("\"data\""));
		Assert.That(json, Does.Contain("\"durationMs\""));
	}

	[Test]
	public void ErrorEnvelopeIncludesErrorDetails()
	{
		var envelope = CliResponseFactory.Error("ping", CliErrorCodes.TargetNotFound, "missing", Stopwatch.StartNew(), new { pid = 4 });
		var json = CliOutput.ToJson(envelope, pretty: false, hideEmpty: true);

		Assert.That(json, Does.Contain("\"ok\":false"));
		Assert.That(json, Does.Contain("\"code\":\"target-not-found\""));
		Assert.That(json, Does.Contain("\"details\""));
	}

	[Test]
	public void PrettyJsonAddsNewLines()
	{
		var envelope = CliResponseFactory.Success("version", new ProductVersionData { ProductName = "DeepFlowTest" }, Stopwatch.StartNew());
		var json = CliOutput.ToJson(envelope, pretty: true, hideEmpty: true);

		Assert.That(json, Does.Contain(System.Environment.NewLine));
	}

	[Test]
	public void HideEmptyPrunesEmptyDiagnostics()
	{
		var envelope = CliResponseFactory.Success("version", new ProductVersionData { ProductName = "DeepFlowTest" }, Stopwatch.StartNew());
		var json = CliOutput.ToJson(envelope, pretty: false, hideEmpty: true);

		Assert.That(json, Does.Not.Contain("diagnostics"));
	}

	[Test]
	public void TextOutputForVersionIsProductName()
	{
		var result = CliTestHost.Run(new[] { "version", "--format", "text" });

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout.Trim(), Is.EqualTo("DeepFlowTest"));
	}
}
