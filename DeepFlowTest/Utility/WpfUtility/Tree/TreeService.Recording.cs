namespace DeepFlowTest.Utility.WpfUtility.Tree;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DeepFlowTest.Contracts;

public sealed partial class TreeService
{
	private static readonly IReadOnlyList<string> RecordingIdentityPropertyNames =
	[
		KnownProperties.Name,
		KnownProperties.AutomationName,
		KnownProperties.AutomationNameAlias,
		KnownProperties.AutomationId,
		KnownProperties.AutomationIdAlias,
		KnownProperties.Text,
		KnownProperties.Content,
		KnownProperties.Header,
		KnownProperties.Title,
		KnownProperties.Uid,
		KnownProperties.IsVisible,
		KnownProperties.IsEnabled,
	];

	internal bool IsUsefulRecordingTarget(object target)
	{
		_ = target ?? throw new ArgumentNullException(nameof(target));

		try
		{
			using var wrapper = TargetObjectWrapper.Create(target);
			if (wrapper.Metadata.CanReceiveActions)
				return true;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
		}

		var properties = propertyExtractor.Extract(target, RecordingIdentityPropertyNames);
		return RecordingIdentityPropertyNames.Any(propertyName => HasUsefulProperty(properties, propertyName));
	}

	internal RecordedTarget DescribeTargetForRecording(
		object target,
		object? rawSource = null,
		IEnumerable<string>? requestedPropertyNames = null)
	{
		_ = target ?? throw new ArgumentNullException(nameof(target));

		using var wrapper = TargetObjectWrapper.Create(target);
		var propertyNames = MergeRecordingPropertyNames(requestedPropertyNames);
		var properties = propertyExtractor.Extract(target, propertyNames);
		var targetId = targetIds.GetOrCreateId(target);
		var summary = BuildRecordingSummary(wrapper.Metadata.DisplayTypeName, properties, targetId);
		return new RecordedTarget
		{
			TargetId = targetId,
			TypeName = wrapper.Metadata.DisplayTypeName,
			FrameworkTypeName = wrapper.Metadata.TargetObjectType,
			RuntimeFamily = wrapper.Metadata.RuntimeFamily,
			Hwnd = wrapper.Metadata.Hwnd,
			Summary = summary,
			SelectorHints = BuildSelectorHints(targetId, properties),
			Properties = properties,
			RawSourceTypeName = rawSource?.GetType().FullName,
		};
	}

	private static IReadOnlyList<string> MergeRecordingPropertyNames(IEnumerable<string>? requestedPropertyNames)
	{
		var names = new List<string>(RecordingIdentityPropertyNames);
		if (requestedPropertyNames is not null)
			names.AddRange(requestedPropertyNames.Where(static name => !string.IsNullOrWhiteSpace(name)));

		return names
			.Select(static name => name.Trim())
			.Distinct(StringComparer.Ordinal)
			.ToArray();
	}

	private static bool HasUsefulProperty(IReadOnlyDictionary<string, object?> properties, string propertyName)
	{
		if (!properties.TryGetValue(propertyName, out var value) || value is null or PropertyExtractionError)
			return false;

		return !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));
	}

	private static string BuildRecordingSummary(string typeName, IReadOnlyDictionary<string, object?> properties, string targetId)
	{
		foreach (var candidate in new[]
		{
			KnownProperties.AutomationId,
			KnownProperties.AutomationIdAlias,
			KnownProperties.AutomationName,
			KnownProperties.AutomationNameAlias,
			KnownProperties.Name,
			KnownProperties.Text,
			KnownProperties.Content,
			KnownProperties.Header,
			KnownProperties.Title,
		})
		{
			if (TryGetString(properties, candidate, out var value))
				return $"{typeName}[{candidate}='{value}']";
		}

		return $"{typeName}[TargetId='{targetId}']";
	}

	private static List<RecordedSelectorHint> BuildSelectorHints(string targetId, IReadOnlyDictionary<string, object?> properties)
	{
		List<RecordedSelectorHint> hints = [];
		AddHint(hints, properties, KnownProperties.AutomationId, "automation-id", 0.98, "--automation-id");
		AddHint(hints, properties, KnownProperties.AutomationIdAlias, "automation-id", 0.98, "--automation-id");
		AddHint(hints, properties, KnownProperties.AutomationName, "automation-name", 0.90, "--name");
		AddHint(hints, properties, KnownProperties.AutomationNameAlias, "automation-name", 0.90, "--name");
		AddHint(hints, properties, KnownProperties.Name, "name", 0.85, "--name");
		AddHint(hints, properties, KnownProperties.Text, "text", 0.75, "--text");
		AddHint(hints, properties, KnownProperties.Content, "content", 0.72, "--property");
		AddHint(hints, properties, KnownProperties.Uid, "uid", 0.80, "--property");
		hints.Add(new RecordedSelectorHint
		{
			Kind = "target-id",
			Confidence = 0.40,
			PropertyName = "TargetId",
			Value = targetId,
			Cli = $"--target {Quote(targetId)}",
		});
		return hints;
	}

	private static void AddHint(
		List<RecordedSelectorHint> hints,
		IReadOnlyDictionary<string, object?> properties,
		string propertyName,
		string kind,
		double confidence,
		string cliOption)
	{
		if (!TryGetString(properties, propertyName, out var value))
			return;

		var cli = cliOption == "--property"
			? $"{cliOption} {Quote(propertyName + "=" + value)}"
			: $"{cliOption} {Quote(value)}";
		hints.Add(new RecordedSelectorHint
		{
			Kind = kind,
			Confidence = confidence,
			PropertyName = propertyName,
			Value = value,
			Cli = cli,
		});
	}

	private static bool TryGetString(IReadOnlyDictionary<string, object?> properties, string propertyName, out string value)
	{
		value = string.Empty;
		if (!properties.TryGetValue(propertyName, out var rawValue) || rawValue is null or PropertyExtractionError)
			return false;

		var text = Convert.ToString(rawValue, CultureInfo.InvariantCulture);
		if (string.IsNullOrWhiteSpace(text))
			return false;

		value = text!;
		return true;
	}

	private static string Quote(string value) =>
		"\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
