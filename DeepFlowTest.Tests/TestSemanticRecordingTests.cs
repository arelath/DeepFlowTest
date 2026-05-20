namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
[NonParallelizable]
public sealed class TestSemanticRecordingTests
{
	private Dictionary<string, string?> parameters = [];

	[SetUp]
	public void SetUp()
	{
		parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
		TestSemanticRecording.ParameterProvider = name =>
			parameters.TryGetValue(name, out var value) ? value : null;
	}

	[TearDown]
	public void TearDown()
	{
		TestSemanticRecording.ResetParameterProviderForTests();
	}

	[Test]
	public void ConfigureSetsAutomaticSemanticRecordingByDefault()
	{
		var options = new AppDriverOptions();

		TestSemanticRecording.Configure(options, "hello world");

		Assert.That(options.AutoSemanticRecordingOutputPath, Is.Not.Null);
		Assert.That(options.AutoSemanticRecordingOutputPath, Does.Contain("hello_world"));
		Assert.That(Path.GetFileName(options.AutoSemanticRecordingOutputPath!), Does.Contain("hello_world"));
		Assert.That(options.AutoSemanticRecordingOptions.IntervalMs, Is.EqualTo(100));
		Assert.That(options.AutoSemanticRecordingOptions.CompactOutput, Is.True);
		Assert.That(options.AutoSemanticRecordingOptions.MaxNodeCount, Is.EqualTo(VisualTreeDefaults.DefaultMaxNodeCount));
		Assert.That(options.AutoSemanticRecordingOptions.PropNames, Does.Contain(KnownProperties.AutomationId));
	}

	[TestCase("0")]
	[TestCase("false")]
	[TestCase("FALSE")]
	[TestCase("no")]
	[TestCase("off")]
	public void ConfigureLeavesOptionsAloneWhenDisabledByTestParameter(string value)
	{
		parameters[TestSemanticRecording.EnabledParameterName] = value;
		var options = new AppDriverOptions();

		TestSemanticRecording.Configure(options, "demo");

		Assert.That(TestSemanticRecording.IsEnabled(), Is.False);
		Assert.That(options.AutoSemanticRecordingOutputPath, Is.Null);
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("1")]
	[TestCase("true")]
	[TestCase("yes")]
	[TestCase("on")]
	[TestCase("maybe")]
	public void IsEnabledDefaultsOnAndOnlyFalseyValuesDisable(string? value)
	{
		if (value is not null)
			parameters[TestSemanticRecording.EnabledParameterName] = value;

		Assert.That(TestSemanticRecording.IsEnabled(), Is.True);
	}

	[Test]
	public void ConfigureUsesOutputDirectoryParameterWhenSpecified()
	{
		var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"test-recordings-{Guid.NewGuid():N}");
		parameters[TestSemanticRecording.OutputDirectoryParameterName] = directory;
		var options = new AppDriverOptions();

		TestSemanticRecording.Configure(options, "default-dir");

		Assert.That(options.AutoSemanticRecordingOutputPath, Is.Not.Null);
		Assert.That(options.AutoSemanticRecordingOutputPath, Does.StartWith(directory));
		Assert.That(Directory.Exists(directory), Is.True);
	}

	[Test]
	public void ConfigureUsesDefaultOutputDirectoryWhenNotSpecified()
	{
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
		parameters[TestSemanticRecording.OutputDirectoryParameterName] = directory;
		var options = new AppDriverOptions();

		TestSemanticRecording.Configure(options, "bad:name with spaces");

		var fileName = Path.GetFileName(options.AutoSemanticRecordingOutputPath!);
		Assert.That(fileName, Does.Contain("bad_name_with_spaces"));
		Assert.That(fileName, Does.Not.Contain(" "));
		Assert.That(fileName, Does.EndWith(".json"));
	}
}
