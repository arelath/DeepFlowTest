namespace DeepFlowTest.Tests;

using System;
using System.IO;
using System.Text;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using Newtonsoft.Json.Linq;
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

		var encoded = options.Encode();
		var decoded = AppDriverPayloadStartupOptions.Decode(encoded);
		var json = DecodeStartupJson(encoded);
		var parsed = JObject.Parse(json);

		Assert.That(parsed["pipeName"]?.Value<string>(), Is.EqualTo(options.PipeName));
		Assert.That(parsed["payloadRoot"]?.Value<string>(), Is.EqualTo(options.PayloadRoot));
		Assert.That(parsed["PipeName"], Is.Null);
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
	public void StartupOptionsRejectDecodeSideMalformedJsonAndUnknownMode()
	{
		Assert.That(
			() => AppDriverPayloadStartupOptions.Decode(EncodeRawStartupJson("{")),
			Throws.TypeOf<ProtocolException>().With.Property(nameof(ProtocolException.ErrorCode)).EqualTo(ProtocolConstants.ErrorCodes.StartupError));

		Assert.That(
			() => AppDriverPayloadStartupOptions.Decode(EncodeRawStartupJson(@"{""pipeName"":""pipe"",""mode"":""OtherMode"",""payloadRoot"":""root"",""protocolVersion"":""1""}")),
			Throws.TypeOf<ProtocolException>().With.Property(nameof(ProtocolException.ErrorCode)).EqualTo(ProtocolConstants.ErrorCodes.StartupError));
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
	public void StartupOptionsJsonFileRejectsMalformedJsonAndUnknownMode()
	{
		var malformedPath = Path.Combine(Path.GetTempPath(), $"deepflowtest-startup-{Guid.NewGuid():N}.json");
		var unknownModePath = Path.Combine(Path.GetTempPath(), $"deepflowtest-startup-{Guid.NewGuid():N}.json");
		File.WriteAllText(malformedPath, "{");
		File.WriteAllText(unknownModePath, @"{""pipeName"":""pipe"",""mode"":""OtherMode"",""payloadRoot"":""root"",""protocolVersion"":""1""}");
		try
		{
			Assert.That(
				() => AppDriverPayloadStartupOptions.Decode(AppDriverPayloadStartupOptions.EncodeJsonFile(malformedPath)),
				Throws.TypeOf<ProtocolException>().With.Property(nameof(ProtocolException.ErrorCode)).EqualTo(ProtocolConstants.ErrorCodes.StartupError));
			Assert.That(
				() => AppDriverPayloadStartupOptions.Decode(AppDriverPayloadStartupOptions.EncodeJsonFile(unknownModePath)),
				Throws.TypeOf<ProtocolException>().With.Property(nameof(ProtocolException.ErrorCode)).EqualTo(ProtocolConstants.ErrorCodes.StartupError));
		}
		finally
		{
			File.Delete(malformedPath);
			File.Delete(unknownModePath);
		}
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
		Assert.That(File.ReadAllText(PayloadLog.CurrentLogPath), Does.Contain("UI availability"));
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

	private static string DecodeStartupJson(string encoded)
	{
		const string prefix = "dft:";
		var base64 = encoded.Substring(prefix.Length).Replace('-', '+').Replace('_', '/');
		switch (base64.Length % 4)
		{
			case 2:
				base64 += "==";
				break;
			case 3:
				base64 += "=";
				break;
		}

		return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
	}

	private static string EncodeRawStartupJson(string json)
	{
		return "dft:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
	}

	private sealed class FakeRuntime : IAppDriverPayloadRuntime
	{
		public bool HasTarget { get; set; } = true;

		public int OneShotCount { get; private set; }

		public int ReusableCount { get; private set; }

		public DeepFlowTest.Utility.UiAvailability GetAvailability()
		{
			return new DeepFlowTest.Utility.UiAvailability
			{
				IsWpfAvailable = HasTarget,
				IsDispatcherAvailable = HasTarget,
				RootCount = HasTarget ? 1 : 0,
			};
		}

		public bool HasSupportedTarget(DeepFlowTest.Utility.UiAvailability availability)
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
