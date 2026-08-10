namespace DeepFlowTest.Utility.WpfUtility.Tree.Wrappers;

internal sealed class ImageTargetObjectWrapper : TargetObjectWrapper
{
	public ImageTargetObjectWrapper(object target)
		: base(target, CreateMetadata(target, TargetObjectKind.Image, "image", canReceiveActions: false))
	{
	}
}
