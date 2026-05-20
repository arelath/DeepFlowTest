namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using DeepFlowTest.Contracts;
using NUnit.Framework;

internal static class TestSemanticRecording
{
	public const string EnabledParameterName = "DeepFlowTestTestRecordings";
	public const string OutputDirectoryParameterName = "DeepFlowTestTestRecordingsDir";

	private static readonly IReadOnlyList<string> DefaultPropertyNames =
	[
		KnownProperties.Name,
		KnownProperties.AutomationName,
		KnownProperties.AutomationId,
		KnownProperties.Text,
		KnownProperties.Content,
		KnownProperties.Header,
		KnownProperties.IsChecked,
		KnownProperties.IsEnabled,
		KnownProperties.IsExpanded,
		KnownProperties.IsOpen,
		KnownProperties.IsSubmenuOpen,
		KnownProperties.IsVisible,
		KnownProperties.Visibility,
	];

	internal static Func<string, string?> ParameterProvider { get; set; } =
		static name => TestContext.Parameters.Get(name, null);

	public static void Configure(AppDriverOptions options, string? label = null)
	{
		_ = options ?? throw new ArgumentNullException(nameof(options));
		if (!IsEnabled())
		{
			options.AutoSemanticRecordingEnabled = false;
			return;
		}

		var outputPath = CreateOutputPath(label);
		options.AutoSemanticRecordingEnabled = true;
		options.AutoSemanticRecordingOutputPath = outputPath;
		options.AutoSemanticRecordingOptions.IntervalMs = 100;
		options.AutoSemanticRecordingOptions.TextIdleMs = 25;
		options.AutoSemanticRecordingOptions.MaxBatchFrames = 20;
		options.AutoSemanticRecordingOptions.PropNames = DefaultPropertyNames;
		options.AutoSemanticRecordingOptions.TimeoutMs = 30_000;
		TestContext.Progress.WriteLine($"DeepFlowTest semantic recording: {outputPath}");
	}

	public static bool IsEnabled()
	{
		var value = ParameterProvider(EnabledParameterName);
		return !IsFalse(value);
	}

	private static string CreateOutputPath(string? label)
	{
		var configuredDirectory = ParameterProvider(OutputDirectoryParameterName);
		var directory = string.IsNullOrWhiteSpace(configuredDirectory)
			? Path.Combine(TestContext.CurrentContext.WorkDirectory, "semantic-recordings")
			: Path.GetFullPath(configuredDirectory);
		Directory.CreateDirectory(directory);

		var testName = TestContext.CurrentContext.Test.FullName;
		if (string.IsNullOrWhiteSpace(testName))
			testName = "test";
		var name = SanitizeFileName(testName);
		if (!string.IsNullOrWhiteSpace(label))
			name = $"{name}-{SanitizeFileName(label)}";
		if (name.Length > 150)
			name = name.Substring(0, 150);

		return Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{name}.json");
	}

	internal static void ResetParameterProviderForTests() =>
		ParameterProvider = static name => TestContext.Parameters.Get(name, null);

	private static bool IsFalse(string? value) =>
		value is not null
		&& (value.Equals("0", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("false", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("no", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("off", StringComparison.OrdinalIgnoreCase));

	private static string SanitizeFileName(string value)
	{
		var invalid = Path.GetInvalidFileNameChars();
		var characters = value.ToCharArray();
		for (var i = 0; i < characters.Length; i++)
		{
			if (Array.IndexOf(invalid, characters[i]) >= 0 || char.IsWhiteSpace(characters[i]))
				characters[i] = '_';
		}

		return new string(characters).Trim('_');
	}
}
