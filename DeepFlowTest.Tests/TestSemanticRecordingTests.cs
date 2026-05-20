namespace DeepFlowTest.Tests;

using System;
using System.IO;
using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
[NonParallelizable]
public sealed class TestSemanticRecordingTests
{
	private string? previousEnabled;
	private string? previousOutputDirectory;

	[SetUp]
	public void SetUp()
	{
		previousEnabled = Environment.GetEnvironmentVariable(TestSemanticRecording.EnabledEnvironmentVariable);
		previousOutputDirectory = Environment.GetEnvironmentVariable(TestSemanticRecording.OutputDirectoryEnvironmentVariable);
		Environment.SetEnvironmentVariable(TestSemanticRecording.EnabledEnvironmentVariable, null);
		Environment.SetEnvironmentVariable(TestSemanticRecording.OutputDirectoryEnvironmentVariable, null);
	}

	[TearDown]
	public void TearDown()
	{
		Environment.SetEnvironmentVariable(TestSemanticRecording.EnabledEnvironmentVariable, previousEnabled);
		Environment.SetEnvironmentVariable(TestSemanticRecording.OutputDirectoryEnvironmentVariable, previousOutputDirectory);
	}

	[Test]
	public void ConfigureLeavesOptionsAloneWhenDisabled()
	{
		var options = new AppDriverOptions();

		TestSemanticRecording.Configure(options, "demo");

		Assert.That(options.AutoSemanticRecordingOutputPath, Is.Null);
	}

	[TestCase("1")]
	[TestCase("true")]
	[TestCase("TRUE")]
	[TestCase("yes")]
	[TestCase("on")]
	public void IsEnabledRecognizesTruthyValues(string value)
	{
		Environment.SetEnvironmentVariable(TestSemanticRecording.EnabledEnvironmentVariable, value);

		Assert.That(TestSemanticRecording.IsEnabled(), Is.True);
	}

	[TestCase("")]
	[TestCase("0")]
	[TestCase("false")]
	[TestCase("no")]
	[TestCase("off")]
	[TestCase("maybe")]
	public void IsEnabledIgnoresFalseyAndUnknownValues(string value)
	{
		Environment.SetEnvironmentVariable(TestSemanticRecording.EnabledEnvironmentVariable, value);

		Assert.That(TestSemanticRecording.IsEnabled(), Is.False);
	}

	[Test]
	public void ConfigureSetsAutomaticSemanticRecordingWhenEnabled()
	{
		var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"test-recordings-{Guid.NewGuid():N}");
		Environment.SetEnvironmentVariable(TestSemanticRecording.EnabledEnvironmentVariable, "true");
		Environment.SetEnvironmentVariable(TestSemanticRecording.OutputDirectoryEnvironmentVariable, directory);
		var options = new AppDriverOptions();

		TestSemanticRecording.Configure(options, "hello world");

		Assert.That(options.AutoSemanticRecordingOutputPath, Is.Not.Null);
		Assert.That(options.AutoSemanticRecordingOutputPath, Does.StartWith(directory));
		Assert.That(Path.GetFileName(options.AutoSemanticRecordingOutputPath!), Does.Contain("hello_world"));
		Assert.That(options.AutoSemanticRecordingOptions.IntervalMs, Is.EqualTo(100));
		Assert.That(options.AutoSemanticRecordingOptions.CompactOutput, Is.True);
		Assert.That(options.AutoSemanticRecordingOptions.MaxNodeCount, Is.EqualTo(VisualTreeDefaults.DefaultMaxNodeCount));
		Assert.That(options.AutoSemanticRecordingOptions.PropNames, Does.Contain(KnownProperties.AutomationId));
	}

	[Test]
	public void ConfigureUsesDefaultOutputDirectoryWhenNotSpecified()
	{
		Environment.SetEnvironmentVariable(TestSemanticRecording.EnabledEnvironmentVariable, "true");
		var options = new AppDriverOptions();

		TestSemanticRecording.Configure(options, "default-dir");

		var expectedDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "semantic-recordings");
		Assert.That(options.AutoSemanticRecordingOutputPath, Is.Not.Null);
		Assert.That(options.AutoSemanticRecordingOutputPath, Does.StartWith(expectedDirectory));
		Assert.That(Directory.Exists(expectedDirectory), Is.True);
	}

	[Test]
	public void ConfigureSanitizesRecordingFileName()
	{
		var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"test-recordings-{Guid.NewGuid():N}");
		Environment.SetEnvironmentVariable(TestSemanticRecording.EnabledEnvironmentVariable, "true");
		Environment.SetEnvironmentVariable(TestSemanticRecording.OutputDirectoryEnvironmentVariable, directory);
		var options = new AppDriverOptions();

		TestSemanticRecording.Configure(options, "bad:name with spaces");

		var fileName = Path.GetFileName(options.AutoSemanticRecordingOutputPath!);
		Assert.That(fileName, Does.Contain("bad_name_with_spaces"));
		Assert.That(fileName, Does.Not.Contain(" "));
		Assert.That(fileName, Does.EndWith(".jsonl"));
	}
}
