namespace DeepFlowTest.Automation;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class SelectorSuggestionService
{
	private readonly TargetIdService targetIds;

	public SelectorSuggestionService(TargetIdService targetIds, VisualTreeSnapshot snapshot)
	{
		this.targetIds = targetIds ?? throw new ArgumentNullException(nameof(targetIds));
		_ = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
	}

	public SelectorSuggestionData Suggest(VisualTreeNodeDto node, bool useShortIds)
	{
		List<SelectorSuggestion> suggestions = [];
		AddPropertySuggestion(suggestions, node, KnownProperties.AutomationId, "--automation-id", 0.98, "Automation ID is usually the most stable selector.");
		AddPropertySuggestion(suggestions, node, KnownProperties.AutomationName, "--name", 0.90, "Automation name is intended for UI automation.");
		AddPropertySuggestion(suggestions, node, KnownProperties.Name, "--name", 0.85, "WPF Name is useful when automation ID is absent.");
		AddPropertySuggestion(suggestions, node, KnownProperties.Uid, "--property", 0.80, "WPF Uid can be stable in localized apps.");
		AddFirstTextSuggestion(suggestions, node);

		suggestions.Add(new SelectorSuggestion
		{
			Kind = "target-id",
			Confidence = 0.40,
			CommandLine = $"--target {Quote(node.TargetId)}",
			Explanation = "Full target IDs are exact but may become stale after UI changes.",
		});
		if (useShortIds)
		{
			suggestions.Add(new SelectorSuggestion
			{
				Kind = "short-id",
				Confidence = 0.40,
				CommandLine = $"--target {Quote(targetIds.GetShortId(node.TargetId))}",
				Explanation = "Short target ID is concise when it is unique in the current snapshot.",
			});
		}

		return new SelectorSuggestionData
		{
			TargetId = node.TargetId,
			ShortId = useShortIds ? targetIds.GetShortId(node.TargetId) : null,
			Suggestions = suggestions,
		};
	}

	private static void AddPropertySuggestion(
		List<SelectorSuggestion> suggestions,
		VisualTreeNodeDto node,
		string propertyName,
		string cliOption,
		double confidence,
		string explanation)
	{
		if (!node.Properties.TryGetValue(propertyName, out var value) || value is null)
			return;

		var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
		if (string.IsNullOrWhiteSpace(text))
			return;

		var cli = cliOption == "--property"
			? $"{cliOption} {Quote(propertyName + "=" + text)}"
			: $"{cliOption} {Quote(text)}";
		suggestions.Add(new SelectorSuggestion
		{
			Kind = propertyName,
			Confidence = confidence,
			CommandLine = cli,
			Explanation = explanation,
		});
	}

	private static void AddFirstTextSuggestion(List<SelectorSuggestion> suggestions, VisualTreeNodeDto node)
	{
		foreach (var property in KnownProperties.TextualIdentityPropertyNames)
		{
			if (!node.Properties.TryGetValue(property, out var value) || value is null)
				continue;

			var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
			if (string.IsNullOrWhiteSpace(text))
				continue;

			suggestions.Add(new SelectorSuggestion
			{
				Kind = property,
				Confidence = 0.75,
				CommandLine = $"--text {Quote(text)}",
				Explanation = $"{property} is a readable fallback when automation properties are absent.",
			});
			return;
		}
	}

	private static string Quote(string value) =>
		"\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

public sealed class SelectorSuggestionData
{
	public string TargetId { get; set; } = string.Empty;

	public string? ShortId { get; set; }

	public IReadOnlyList<SelectorSuggestion> Suggestions { get; set; } = [];
}

public sealed class SelectorSuggestion
{
	public string Kind { get; set; } = string.Empty;

	public double Confidence { get; set; }

	[JsonPropertyName("cli")]
	public string CommandLine { get; set; } = string.Empty;

	public string Explanation { get; set; } = string.Empty;
}
