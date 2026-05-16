namespace DeepFlowTest.Interop;

using System.Collections.Generic;

public sealed record class VisualTreeNodeDto
{
	public string TargetId { get; set; } = string.Empty;

	public string? ParentId { get; set; }

	public List<string> ChildIds { get; set; } = [];

	public bool IsRoot { get; set; }

	public int Depth { get; set; }

	public int SiblingIndex { get; set; }

	public string TypeName { get; set; } = string.Empty;

	public string? FrameworkTypeName { get; set; }

	public string? TargetKind { get; set; }

	public string? RuntimeFamily { get; set; }

	public bool CanReceiveActions { get; set; }

	public long? Hwnd { get; set; }

	public Dictionary<string, object?> Properties { get; set; } = [];
}
