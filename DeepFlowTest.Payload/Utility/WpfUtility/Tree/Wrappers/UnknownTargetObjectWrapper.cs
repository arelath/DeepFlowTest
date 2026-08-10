namespace DeepFlowTest.Utility.WpfUtility.Tree.Wrappers;

internal sealed class UnknownTargetObjectWrapper : TargetObjectWrapper
{
	public UnknownTargetObjectWrapper(object target)
		: base(target, CreateMetadata(target, TargetObjectKind.Unknown, "unknown", canReceiveActions: false))
	{
	}
}
