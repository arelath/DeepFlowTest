namespace DeepFlowTest.Mcp.Configuration;

internal sealed class McpGuiSettings
{
	public int SchemaVersion { get; set; } = 1;

	public McpGuiTargetSettings Target { get; set; } = new();

	public McpGuiPolicySettings Policy { get; set; } = new();

	public McpGuiVirtualPointerSettings VirtualPointer { get; set; } = new();

	public string? ActivityFilter { get; set; }
}

internal sealed class McpGuiTargetSettings
{
	public string AttachPidText { get; set; } = string.Empty;

	public string? AttachProcessName { get; set; }

	public string? AttachWindowTitle { get; set; }

	public string? LaunchPath { get; set; }

	public string? LaunchArguments { get; set; }

	public bool TerminateOnDetach { get; set; }
}

internal sealed class McpGuiPolicySettings
{
	public bool AllowLaunch { get; set; }

	public bool AllowActions { get; set; }

	public bool AllowArbitraryInvoke { get; set; }

	public bool AllowFileWrites { get; set; }
}

internal sealed class McpGuiVirtualPointerSettings
{
	public bool Enabled { get; set; }

	public bool ShowClickRipples { get; set; } = true;

	public bool ShowDragTrail { get; set; } = true;

	public bool IncludeInScreenshots { get; set; }

	public string HideDelayMs { get; set; } = "800";
}
