namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using DeepFlowTest.Interop;

public sealed class SelectorSuggestionService
{
	private readonly CliTargetIdService targetIds;
	private readonly SnapshotRelationships relationships;

	public SelectorSuggestionService(CliTargetIdService targetIds, VisualTreeSnapshot snapshot)
	{
		this.targetIds = targetIds ?? throw new ArgumentNullException(nameof(targetIds));
		relationships = SnapshotRelationships.Create(snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
	}

	public SelectorSuggestionData Suggest(VisualTreeNodeDto node, bool useShortIds)
	{
		var suggestions = new List<SelectorSuggestion>();
		AddPropertySuggestion(suggestions, node, "AutomationProperties.AutomationId", "--automation-id", 0.98, "Automation ID is usually the most stable selector.");
		AddPropertySuggestion(suggestions, node, "AutomationProperties.Name", "--name", 0.90, "Automation name is intended for UI automation.");
		AddPropertySuggestion(suggestions, node, "Name", "--name", 0.85, "WPF Name is useful when automation ID is absent.");
		AddPropertySuggestion(suggestions, node, "Uid", "--property", 0.80, "WPF Uid can be stable in localized apps.");
		AddFirstTextSuggestion(suggestions, node);

		suggestions.Add(new SelectorSuggestion
		{
			Kind = "path",
			Confidence = 0.45,
			Cli = $"--path \"{relationships.PathOf(node.TargetId)}\"",
			Explanation = "Structural path is useful as a fallback when semantic properties are absent.",
		});
		suggestions.Add(new SelectorSuggestion
		{
			Kind = "target-id",
			Confidence = 0.40,
			Cli = $"--target \"{node.TargetId}\"",
			Explanation = "Full target IDs are exact but may become stale after UI changes.",
		});
		if (useShortIds)
		{
			suggestions.Add(new SelectorSuggestion
			{
				Kind = "short-id",
				Confidence = 0.40,
				Cli = $"--target \"{targetIds.GetShortId(node.TargetId)}\"",
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
			? $"{cliOption} \"{propertyName}={text}\""
			: $"{cliOption} \"{text}\"";
		suggestions.Add(new SelectorSuggestion
		{
			Kind = propertyName,
			Confidence = confidence,
			Cli = cli,
			Explanation = explanation,
		});
	}

	private static void AddFirstTextSuggestion(List<SelectorSuggestion> suggestions, VisualTreeNodeDto node)
	{
		foreach (var property in new[] { "Text", "Content", "Header", "Title" })
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
				Cli = $"--text \"{text}\"",
				Explanation = $"{property} is a readable fallback when automation properties are absent.",
			});
			return;
		}
	}
}

public sealed class SelectorSuggestionData
{
	public string TargetId { get; set; } = string.Empty;

	public string? ShortId { get; set; }

	public IReadOnlyList<SelectorSuggestion> Suggestions { get; set; } = Array.Empty<SelectorSuggestion>();
}

public sealed class SelectorSuggestion
{
	public string Kind { get; set; } = string.Empty;

	public double Confidence { get; set; }

	public string Cli { get; set; } = string.Empty;

	public string Explanation { get; set; } = string.Empty;
}
