namespace DeepFlowTest.Cli.Tests;

using System.IO;
using NUnit.Framework;

[TestFixture]
public sealed class CliDefaultsTests
{
	[Test]
	public void BuiltInDefaultsMatchMilestone()
	{
		var defaults = new CliDefaults();

		Assert.That(defaults.TimeoutMs, Is.EqualTo(10_000));
		Assert.That(defaults.OutputFormat, Is.EqualTo("json"));
		Assert.That(defaults.HideEmpty, Is.True);
		Assert.That(defaults.UseShortIds, Is.True);
		Assert.That(defaults.AfterSnapshot, Is.EqualTo("none"));
		Assert.That(defaults.TreeShape, Is.EqualTo("flat"));
		Assert.That(defaults.TreeMaxDepth, Is.EqualTo(-1));
		Assert.That(defaults.TreeLimit, Is.EqualTo(1000));
		Assert.That(defaults.PropertyNames, Is.EquivalentTo(new[]
		{
			"Name",
			"AutomationProperties.Name",
			"AutomationProperties.AutomationId",
			"Text",
			"Content",
			"IsVisible",
			"IsEnabled",
		}));
		Assert.That(defaults.FindLimit, Is.EqualTo(50));
		Assert.That(defaults.WaitIntervalMs, Is.EqualTo(250));
		Assert.That(defaults.WaitMatchCount, Is.EqualTo(1));
		Assert.That(defaults.StreamIntervalMs, Is.EqualTo(1000));
		Assert.That(defaults.ScreenshotFormat, Is.EqualTo("png"));
		Assert.That(defaults.KeyDelayMs, Is.EqualTo(50));
		Assert.That(defaults.EnsureForeground, Is.True);
	}

	[Test]
	public void DefaultsFileValuesOverrideBuiltIns()
	{
		var store = new CliDefaultsStore(CliTestHost.CreateTempConfigPath());
		store.Set("timeoutMs", "2500");
		store.Set("outputFormat", "text");

		var defaults = store.Load();

		Assert.That(defaults.TimeoutMs, Is.EqualTo(2500));
		Assert.That(defaults.OutputFormat, Is.EqualTo("text"));
	}

	[Test]
	public void ConfigCommandsCreateClearAndResetDefaults()
	{
		var path = CliTestHost.CreateTempConfigPath();
		var store = new CliDefaultsStore(path);
		var services = CliTestHost.CreateServices(defaultsStore: store);

		var set = CliTestHost.Run(new[] { "config", "set", "timeoutMs", "333" }, services);
		var clear = CliTestHost.Run(new[] { "config", "clear", "timeoutMs" }, services);
		var reset = CliTestHost.Run(new[] { "config", "reset" }, services);

		Assert.That(set.ExitCode, Is.EqualTo(0));
		Assert.That(clear.ExitCode, Is.EqualTo(0));
		Assert.That(reset.ExitCode, Is.EqualTo(0));
		Assert.That(File.Exists(path), Is.False);
		Assert.That(store.Load().TimeoutMs, Is.EqualTo(10_000));
	}

	[Test]
	public void ConfigSetBindsPositionalsAfterOutputOptions()
	{
		var store = new CliDefaultsStore(CliTestHost.CreateTempConfigPath());
		var services = CliTestHost.CreateServices(defaultsStore: store);

		var result = CliTestHost.Run(new[] { "config", "set", "--pretty", "timeoutMs", "333" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(store.Load().TimeoutMs, Is.EqualTo(333));
	}

	[Test]
	public void StringListConfigParsingErrorsMapToCliExceptionsAndNullClears()
	{
		var store = new CliDefaultsStore(CliTestHost.CreateTempConfigPath());

		Assert.That(
			() => store.Set("propertyNames", "[1]"),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.InvalidArguments));

		store.Set("timeoutMs", "333");
		store.Set("timeoutMs", "null");

		Assert.That(store.Load().TimeoutMs, Is.EqualTo(10_000));
	}

	[Test]
	public void ConfigResetSucceedsAfterMalformedConfig()
	{
		var path = CliTestHost.CreateTempConfigPath();
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "{");
		var services = CliTestHost.CreateServices(defaultsStore: new CliDefaultsStore(path));

		var result = CliTestHost.Run(new[] { "config", "reset" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(File.Exists(path), Is.False);
	}

	[Test]
	public void MalformedConfigMapsToInvalidConfig()
	{
		var path = CliTestHost.CreateTempConfigPath();
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "{");
		var services = CliTestHost.CreateServices(defaultsStore: new CliDefaultsStore(path));

		var result = CliTestHost.Run(new[] { "version" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(1));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"invalid-config\""));
	}

	[Test]
	public void InvalidEnumValueIsRejected()
	{
		var store = new CliDefaultsStore(CliTestHost.CreateTempConfigPath());

		Assert.That(() => store.Set("outputFormat", "yaml"), Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.InvalidConfig));
	}
}
