namespace DeepFlowTest.Interop;

using System.Collections.Generic;

public sealed class ExpressionMatcherPayload
{
	public string ExpressionJson { get; set; } = string.Empty;

	public string ExpressionText { get; set; } = string.Empty;

	public string ExpressionHash { get; set; } = string.Empty;

	public Dictionary<string, object?> ClosureValues { get; set; } = new();
}
