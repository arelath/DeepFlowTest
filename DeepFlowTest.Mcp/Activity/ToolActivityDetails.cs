namespace DeepFlowTest.Mcp.Activity;

internal sealed record class ToolActivityDetails
{
	public object? Parameters { get; init; }

	public object? Result { get; init; }

	public object? Error { get; init; }
}
