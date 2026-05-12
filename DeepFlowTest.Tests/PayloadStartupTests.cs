namespace DeepFlowTest.Tests;

using System;
using System.IO;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class PayloadStartupTests
{
	[TearDown]
	public void TearDown()
	{
		AppDriverPayload.ResetRuntimeForTests();
	}

	[Test]
	public void OneShotStartupOptionsRoundTrip()
	{
		var options = CreateOptions(PayloadStartupModes.OneShotDriver);

		var decoded = AppDriverPayloadStartupOptions.Decode(options.Encode());

		Assert.That(decoded.PipeName, Is.EqualTo(options.PipeName));
		Assert.That(decoded.Mode, Is.EqualTo(PayloadStartupModes.OneShotDriver));
		Assert.That(decoded.PayloadRoot, Is.EqualTo(options.PayloadRoot));
		Assert.That(decoded.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));
	}

	[Test]
	public void ReusableStartupOptionsRoundTrip()
	{
		var options = CreateOptions(PayloadStartupModes.ReusableCli);

		var decoded = AppDriverPayloadStartupOptions.Decode(options.Encode());

		Assert.That(decoded.Mode, Is.EqualTo(PayloadStartupModes.ReusableCli));
	}

	[Test]
	public void StartupOptionsRejectMalformedAndUnsupportedValues()
	{
		Assert.That(() => AppDriverPayloadStartupOptions.Decode("dft:not-base64"), Throws.TypeOf<ProtocolException>());
		Assert.That(() => AppDriverPayloadStartupOptions.Decode("dft:e30"), Throws.TypeOf<ProtocolException>());

		var unknownMode = CreateOptions("OtherMode");
		Assert.That(() => unknownMode.Encode(), Throws.TypeOf<ProtocolException>());

		var unsupportedProtocol = CreateOptions(PayloadStartupModes.OneShotDriver);
		unsupportedProtocol.ProtocolVersion = "999";
		Assert.That(() => unsupportedProtocol.Encode(), Throws.TypeOf<ProtocolException>());
	}

	[Test]
	public void StartupOptionsDecodeTempJsonFileAndRejectMissingFile()
	{
		var path = Path.Combine(Path.GetTempPath(), $"deepflowtest-startup-{Guid.NewGuid():N}.json");
		File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(CreateOptions(PayloadStartupModes.OneShotDriver)));
		try
		{
			var decoded = AppDriverPayloadStartupOptions.Decode(AppDriverPayloadStartupOptions.EncodeJsonFile(path));

			Assert.That(decoded.PipeName, Does.StartWith("deepflowtest-test-"));
		}
		finally
		{
			File.Delete(path);
		}

		Assert.That(
			() => AppDriverPayloadStartupOptions.Decode(AppDriverPayloadStartupOptions.EncodeJsonFile(path)),
			Throws.TypeOf<ProtocolException>());
	}

	[Test]
	public void PayloadStartCallsOneShotRuntimeWithParsedOptions()
	{
		var runtime = new FakeRuntime();
		AppDriverPayload.ConfigureRuntimeForTests(runtime);

		var exitCode = AppDriverPayload.Start(CreateOptions(PayloadStartupModes.OneShotDriver).Encode());

		Assert.That(exitCode, Is.EqualTo(0));
		Assert.That(runtime.OneShotCount, Is.EqualTo(1));
		Assert.That(runtime.ReusableCount, Is.EqualTo(0));
	}

	[Test]
	public void PayloadStartCallsReusableRuntime()
	{
		var runtime = new FakeRuntime();
		AppDriverPayload.ConfigureRuntimeForTests(runtime);

		var exitCode = AppDriverPayload.Start(CreateOptions(PayloadStartupModes.ReusableCli).Encode());

		Assert.That(exitCode, Is.EqualTo(0));
		Assert.That(runtime.ReusableCount, Is.EqualTo(1));
	}

	[Test]
	public void UnsupportedTargetStartupWritesLogAndReturnsFailure()
	{
		var runtime = new FakeRuntime { HasTarget = false };
		AppDriverPayload.ConfigureRuntimeForTests(runtime);

		var exitCode = AppDriverPayload.Start(CreateOptions(PayloadStartupModes.OneShotDriver).Encode());

		Assert.That(exitCode, Is.EqualTo(1));
		Assert.That(File.ReadAllText(PayloadLog.CurrentLogPath), Does.Contain("Unsupported target"));
	}

	[Test]
	public void PayloadLogPathIsDeterministicFromPipeAndPid()
	{
		var first = PayloadLog.GetLogPath("pipe/with:chars", 1234);
		var second = PayloadLog.GetLogPath("pipe/with:chars", 1234);

		Assert.That(second, Is.EqualTo(first));
		Assert.That(Path.GetFileName(first), Is.EqualTo("pipe_with_chars-1234.log"));
	}

	private static AppDriverPayloadStartupOptions CreateOptions(string mode)
	{
		return new AppDriverPayloadStartupOptions
		{
			PipeName = $"deepflowtest-test-{Guid.NewGuid():N}",
			Mode = mode,
			PayloadRoot = AppContext.BaseDirectory,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};
	}

	private sealed class FakeRuntime : IAppDriverPayloadRuntime
	{
		public bool HasTarget { get; set; } = true;

		public int OneShotCount { get; private set; }

		public int ReusableCount { get; private set; }

		public bool HasSupportedTarget()
		{
			return HasTarget;
		}

		public void StartOneShot(AppDriverPayloadStartupOptions options)
		{
			OneShotCount++;
		}

		public ReusablePipeSession StartReusable(AppDriverPayloadStartupOptions options)
		{
			ReusableCount++;
			return new ReusablePipeSession(options.PipeName, _ => { });
		}
	}
}
