namespace DeepFlowTest.Utility.WpfUtility.Tree.Wrappers;

internal sealed class SystemResourceTargetObjectWrapper : TargetObjectWrapper
{
	public SystemResourceTargetObjectWrapper(SystemResourceRoot target)
		: base(
			target,
			CreateMetadata(
				target,
				TargetObjectKind.SystemResource,
				"wpf",
				canReceiveActions: false,
				displayTypeName: "SystemResources",
				targetObjectType: typeof(SystemResourceRoot).FullName))
	{
	}
}
