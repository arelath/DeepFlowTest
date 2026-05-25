namespace DeepFlowTest.Mcp.Activity;

internal interface IMcpActivitySink
{
	void Publish(McpActivityEvent activity);
}
