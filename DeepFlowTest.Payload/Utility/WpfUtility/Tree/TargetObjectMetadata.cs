namespace DeepFlowTest.Utility.WpfUtility.Tree;

public sealed class TargetObjectMetadata
{
	public TargetObjectKind Kind { get; set; }

	public string TargetObjectType { get; set; } = string.Empty;

	public string DisplayTypeName { get; set; } = string.Empty;

	public string RuntimeFamily { get; set; } = string.Empty;

	public long? Hwnd { get; set; }

	public bool CanReceiveActions { get; set; }
}
