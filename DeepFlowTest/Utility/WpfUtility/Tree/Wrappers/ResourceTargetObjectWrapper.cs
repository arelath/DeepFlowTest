namespace DeepFlowTest.Utility.WpfUtility.Tree.Wrappers;

using System.Windows;

internal sealed class ResourceTargetObjectWrapper : TargetObjectWrapper
{
	public ResourceTargetObjectWrapper(ResourceDictionary target)
		: base(target, CreateMetadata(target, TargetObjectKind.Resource, "wpf", canReceiveActions: false))
	{
	}
}
