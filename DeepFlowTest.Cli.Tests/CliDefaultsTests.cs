namespace DeepFlowTest.Cli.Tests;

using System.IO;
using System.Text.Json.Nodes;
using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class CliDefaultsTests
{
	[Test]
	public void BuiltInDefaultsMatchMilestone()
	{
		var defaults = new CliDefaults();

		Assert.That(defaults.TimeoutMs, Is.EqualTo(TimeoutDefaults.CliCommandTimeoutMs));
		Assert.That(defaults.SchemaVersion, Is.EqualTo(1));
		Assert.That(defaults.Common.TimeoutMs, Is.EqualTo(TimeoutDefaults.CliCommandTimeoutMs));
		Assert.That(defaults.Commands.Tree.Shape, Is.EqualTo(TreeShape.Flat));
		Assert.That(defaults.OutputFormat, Is.EqualTo("json"));
		Assert.That(defaults.HideEmpty, Is.True);
		Assert.That(defaults.UseShortIds, Is.True);
		Assert.That(defaults.AfterSnapshot, Is.EqualTo("none"));
		Assert.That(defaults.TreeShape, Is.EqualTo("flat"));
		Assert.That(defaults.TreeMaxDepth, Is.EqualTo(-1));
		Assert.That(defaults.TreeLimit, Is.EqualTo(VisualTreeDefaults.DefaultMaxNodeCount));
		Assert.That(defaults.PropertyNames, Is.EquivalentTo(KnownProperties.DefaultVisualTreePropertyNames));
		Assert.That(defaults.FindLimit, Is.EqualTo(50));
		Assert.That(defaults.WaitIntervalMs, Is.EqualTo(TimeoutDefaults.CliWaitIntervalMs));
		Assert.That(defaults.WaitMatchCount, Is.EqualTo(1));
		Assert.That(defaults.StreamIntervalMs, Is.EqualTo(TimeoutDefaults.StreamIntervalMs));
		Assert.That(defaults.ScreenshotFormat, Is.EqualTo("png"));
		Assert.That(defaults.KeyDelayMs, Is.EqualTo(TimeoutDefaults.KeyboardDelayMs));
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
	public void LegacyDotPathAliasesReadWriteAndClearFlatDefaults()
	{
		var store = new CliDefaultsStore(CliTestHost.CreateTempConfigPath());

		store.Set("commands.tree.limit", "25");
		store.Set("common.format", "text");

		Assert.That(store.Load().TreeLimit, Is.EqualTo(25));
		Assert.That(store.Load().OutputFormat, Is.EqualTo("text"));
		Assert.That(((JsonValue)store.Get("commands.tree.limit")!).GetValue<int>(), Is.EqualTo(25));

		store.Clear("commands.tree.limit");

		Assert.That(store.Load().TreeLimit, Is.EqualTo(VisualTreeDefaults.DefaultMaxNodeCount));
	}

	[Test]
	public void ScreenshotFormatDefaultsUseSharedImageFormatContract()
	{
		var store = new CliDefaultsStore(CliTestHost.CreateTempConfigPath());

		store.Set("screenshotFormat", "jpg");
		Assert.That(store.Load().ScreenshotFormat, Is.EqualTo("jpeg"));

		store.Set("screenshotFormat", "bmp");
		Assert.That(store.Load().ScreenshotFormat, Is.EqualTo("bmp"));

		store.Set("screenshotFormat", "gif");
		Assert.That(store.Load().ScreenshotFormat, Is.EqualTo("gif"));
	}

	[Test]
	public void ConfigCommandsCreateClearAndResetDefaults()
	{
		var path = CliTestHost.CreateTempConfigPath();
		var store = new CliDefaultsStore(path);
		var services = CliTestHost.CreateServices(defaultsStore: store);

		var set = CliTestHost.Run(new[] { "config", "set", "timeoutMs", "333" }, services);
		var clear = CliTestHost.Run(new[] { "config", "clear", "timeoutMs" }, services);
		var reset = CliTestHost.Run(new[] { "config", "reset", "--yes" }, services);

		Assert.That(set.ExitCode, Is.EqualTo(0));
		Assert.That(clear.ExitCode, Is.EqualTo(0));
		Assert.That(reset.ExitCode, Is.EqualTo(0));
		Assert.That(File.Exists(path), Is.True);
		Assert.That(store.Load().TimeoutMs, Is.EqualTo(TimeoutDefaults.CliCommandTimeoutMs));
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
	public void StringListConfigJsonParsingErrorsMapToCliExceptionsAndNullClears()
	{
		var store = new CliDefaultsStore(CliTestHost.CreateTempConfigPath());

		Assert.That(
			() => store.Set("propertyNames", "[1]", json: true),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.InvalidArguments));

		store.Set("common.process", "DeepFlowTestHarness");
		store.Set("common.process", "null");

		Assert.That(store.Load().Common.Process, Is.Null);
	}

	[Test]
	public void ConfigSetJsonPreservesNestedSchema()
	{
		var path = CliTestHost.CreateTempConfigPath();
		var store = new CliDefaultsStore(path);
		var services = CliTestHost.CreateServices(defaultsStore: store);

		var result = CliTestHost.Run(new[] { "config", "set", "commands.stream.props", "[\"Name\",\"Text\"]", "--json" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(store.Load().Commands.Stream.Props, Is.EqualTo(new[] { KnownProperties.Name, KnownProperties.Text }));
		Assert.That(File.ReadAllText(path), Does.Contain("\"commands\""));
		Assert.That(File.ReadAllText(path), Does.Contain("\"stream\""));
	}

	[Test]
	public void ConfigResetSucceedsAfterMalformedConfig()
	{
		var path = CliTestHost.CreateTempConfigPath();
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "{");
		var services = CliTestHost.CreateServices(defaultsStore: new CliDefaultsStore(path));

		var result = CliTestHost.Run(new[] { "config", "reset", "--yes" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(File.Exists(path), Is.True);
		Assert.That(new CliDefaultsStore(path).Load().SchemaVersion, Is.EqualTo(1));
	}

	[Test]
	public void ConfigResetRequiresConfirmation()
	{
		var services = CliTestHost.CreateServices(defaultsStore: new CliDefaultsStore(CliTestHost.CreateTempConfigPath()));

		var result = CliTestHost.Run(new[] { "config", "reset" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(1));
		Assert.That(result.Stdout, Does.Contain("requires --yes"));
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
		Assert.That(() => store.Set("commands.tree.shape", "tree"), Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.InvalidConfig));
	}
}
