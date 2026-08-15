namespace DeepFlowTest.Automation;

using System.Collections.Generic;

public sealed class ElementSelector
{
	public string? TargetId { get; set; }

	public string? TypeName { get; set; }

	public string? TypeContains { get; set; }

	public string? Name { get; set; }

	public string? AutomationId { get; set; }

	public string? Text { get; set; }

	public KeyValuePair<string, string>? PropertyEquals { get; set; }

	public KeyValuePair<string, string>? PropertyContains { get; set; }

	public KeyValuePair<string, string>? PropertyRegex { get; set; }

	public bool? Visible { get; set; }

	public bool? Enabled { get; set; }

	public bool CaseSensitive { get; set; }

	public bool First { get; set; }

	public int? Index { get; set; }

	public bool IsEmpty =>
		string.IsNullOrWhiteSpace(TargetId)
		&& string.IsNullOrWhiteSpace(TypeName)
		&& string.IsNullOrWhiteSpace(TypeContains)
		&& string.IsNullOrWhiteSpace(Name)
		&& string.IsNullOrWhiteSpace(AutomationId)
		&& string.IsNullOrWhiteSpace(Text)
		&& PropertyEquals is null
		&& PropertyContains is null
		&& PropertyRegex is null
		&& !Visible.HasValue
		&& !Enabled.HasValue;

}
