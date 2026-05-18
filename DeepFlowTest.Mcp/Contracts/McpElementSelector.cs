namespace DeepFlowTest.Mcp.Contracts;

using System.Collections.Generic;
using DeepFlowTest.Cli;

internal sealed record class McpElementSelector
{
	public string? TargetId { get; init; }

	public string? TypeName { get; init; }

	public string? TypeContains { get; init; }

	public string? Name { get; init; }

	public string? AutomationId { get; init; }

	public string? Text { get; init; }

	public KeyValuePair<string, string>? PropertyEquals { get; init; }

	public KeyValuePair<string, string>? PropertyContains { get; init; }

	public KeyValuePair<string, string>? PropertyRegex { get; init; }

	public bool? Visible { get; init; }

	public bool? Enabled { get; init; }

	public bool CaseSensitive { get; init; }

	public bool First { get; init; }

	public int? Index { get; init; }

	public ElementSelector ToCliSelector() =>
		new()
		{
			TargetId = TargetId,
			TypeName = TypeName,
			TypeContains = TypeContains,
			Name = Name,
			AutomationId = AutomationId,
			Text = Text,
			PropertyEquals = PropertyEquals,
			PropertyContains = PropertyContains,
			PropertyRegex = PropertyRegex,
			Visible = Visible,
			Enabled = Enabled,
			CaseSensitive = CaseSensitive,
			First = First,
			Index = Index,
		};
}
