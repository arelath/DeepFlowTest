namespace DeepFlowTest.Cli.Tests;

using System.IO;
using NUnit.Framework;

[TestFixture]
public sealed class ScreenshotCommandHandlerTests
{
	[Test]
	public void ScreenshotWritesFileAndReportsMetadata()
	{
		var path = Path.Combine(Path.GetTempPath(), "DeepFlowTest.Cli.Tests", System.Guid.NewGuid().ToString("N"), "capture.png");
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "screenshot", "--pid", "1234", "--output", path }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(File.Exists(path), Is.True);
		Assert.That(result.Stdout, Does.Contain("\"width\":2"));
		Assert.That(result.Stdout, Does.Not.Contain("bytesBase64"));
	}

	[Test]
	public void ScreenshotAcceptsCompatOutAlias()
	{
		var path = Path.Combine(Path.GetTempPath(), "DeepFlowTest.Cli.Tests", System.Guid.NewGuid().ToString("N"), "capture.png");
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "screenshot", "--pid", "1234", "--out", path }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(File.Exists(path), Is.True);
	}

	[Test]
	public void ScreenshotCanIncludeBase64AndResolveShortTarget()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "screenshot", "--pid", "1234", "--target", "0002", "--base64" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"targetId\":\"button-0002\""));
		Assert.That(result.Stdout, Does.Contain("\"bytesBase64\":\"AQIDBA==\""));
	}

	[Test]
	public void ScreenshotCanResolveSelectorTarget()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "screenshot", "--pid", "1234", "--automation-id", "SubmitButton", "--base64" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"targetId\":\"button-0002\""));
	}

	[Test]
	public void FailedAndMalformedScreenshotPayloadsMapToStableErrors()
	{
		var service = new ScreenshotFileService();

		Assert.That(
			() => service.Process(new DeepFlowTest.Contracts.ScreenshotCommandResponse
			{
				Success = false,
				ErrorCode = DeepFlowTest.Contracts.ProtocolConstants.ErrorCodes.StaleTarget,
				Error = "stale",
			}, new ScreenshotFileOptions()),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.StaleTarget));
		Assert.That(
			() => service.Process(new DeepFlowTest.Contracts.ScreenshotCommandResponse { BytesBase64 = "not-base64" }, new ScreenshotFileOptions()),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.ProtocolError));
	}

	[Test]
	public void UnsupportedImageFormatMapsToInvalidArguments()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var result = CliTestHost.Run(new[] { "screenshot", "--pid", "1234", "--image-format", "tiff" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(1));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"invalid-arguments\""));
	}
}
