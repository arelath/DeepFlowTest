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
	public void ResponseFactoryCanPopulateDiagnostics()
	{
		var envelope = CliResponseFactory.Success("ping", new { ok = true }, Stopwatch.StartNew(), new() { ["pipe"] = "p" });
		var json = CliOutput.ToJson(envelope, pretty: false, hideEmpty: true);

		Assert.That(json, Does.Contain("\"diagnostics\""));
		Assert.That(json, Does.Contain("\"pipe\":\"p\""));
	}

	[Test]
	public void HideEmptyPreservesRequiredArrays()
	{
		var envelope = CliResponseFactory.Success("processes", new ProcessListData(), Stopwatch.StartNew());
		var json = CliOutput.ToJson(envelope, pretty: false, hideEmpty: true);

		Assert.That(json, Does.Contain("\"processes\":[]"));
	}

	[Test]
	public void TextOutputForVersionIsProductName()
	{
		var result = CliTestHost.Run(new[] { "version", "--format", "text" });

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout.Trim(), Is.EqualTo("DeepFlowTest"));
	}

	[Test]
	public void TextOutputForPingAndPipeStatusIsStable()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var ping = CliTestHost.Run(new[] { "ping", "--pid", "1234", "--format", "text" }, services);
		var status = CliTestHost.Run(new[] { "pipe", "status", "--pid", "1234", "--format", "text" }, services);

		Assert.That(ping.Stdout, Does.Contain("process: 1234"));
		Assert.That(status.Stdout, Does.Contain("pipe: fake-pipe"));
	}
}
