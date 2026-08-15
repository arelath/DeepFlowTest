namespace DeepFlowTest.Cli;

using System.Collections.Generic;

internal static class ElementSelectorParser
{
	public static DeepFlowTest.Automation.ElementSelector FromArgs(string[] args) => FromArgs(args, prefix: null);

	public static DeepFlowTest.Automation.ElementSelector FromArgs(string[] args, string? prefix)
	{
		var target = Option(prefix, "target");
		var targetId = Option(prefix, "target-id");
		var type = Option(prefix, "type");
		var typeContains = Option(prefix, "type-contains");
		var name = Option(prefix, "name");
		var automationId = Option(prefix, "automation-id");
		var text = Option(prefix, "text");
		var selectorText = Option(prefix, "selector-text");
		var matchProperty = Option(prefix, "match-property");
		var prop = Option(prefix, "prop");
		var propertyContains = Option(prefix, "property-contains");
		var contains = Option(prefix, "contains");
		var propertyRegex = Option(prefix, "property-regex");
		var regex = Option(prefix, "regex");
		var visible = Option(prefix, "visible");
		var requireVisible = Option(prefix, "require-visible");
		var enabled = Option(prefix, "enabled");
		var requireEnabled = Option(prefix, "require-enabled");
		var caseSensitive = Option(prefix, "case-sensitive");
		var first = Option(prefix, "first");
		var indexName = Option(prefix, "index");

		int? index = CliArgumentReader.GetOption(args, indexName) is null ? null : CliArgumentReader.GetInt(args, indexName, 0);
		if (index.HasValue && index.Value < 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"{indexName} must be a non-negative zero-based index.");

		return new DeepFlowTest.Automation.ElementSelector
		{
			TargetId = CliArgumentReader.GetOption(args, target, targetId),
			TypeName = CliArgumentReader.GetOption(args, type),
			TypeContains = CliArgumentReader.GetOption(args, typeContains),
			Name = CliArgumentReader.GetOption(args, name),
			AutomationId = CliArgumentReader.GetOption(args, automationId),
			Text = CliArgumentReader.GetOption(args, selectorText, text),
			PropertyEquals = GetPropertyEquals(args, prefix, matchProperty, prop),
			PropertyContains = CliArgumentReader.GetKeyValue(args, propertyContains, contains),
			PropertyRegex = CliArgumentReader.GetKeyValue(args, propertyRegex, regex),
			Visible = CliArgumentReader.HasOption(args, visible) || CliArgumentReader.HasOption(args, requireVisible) ? true : null,
			Enabled = CliArgumentReader.HasOption(args, enabled) || CliArgumentReader.HasOption(args, requireEnabled) ? true : null,
			CaseSensitive = CliArgumentReader.HasOption(args, caseSensitive),
			First = CliArgumentReader.HasOption(args, first),
			Index = index,
		};
	}

	private static string Option(string? prefix, string name) =>
		string.IsNullOrWhiteSpace(prefix) ? "--" + name : "--" + prefix + "-" + name;

	private static KeyValuePair<string, string>? GetPropertyEquals(
		IReadOnlyList<string> args,
		string? prefix,
		string matchProperty,
		string prop) =>
		string.IsNullOrWhiteSpace(prefix)
			? CliArgumentReader.GetKeyValue(args, matchProperty, prop)
			: CliArgumentReader.GetKeyValue(args, matchProperty, Option(prefix, "property"), prop);
}
