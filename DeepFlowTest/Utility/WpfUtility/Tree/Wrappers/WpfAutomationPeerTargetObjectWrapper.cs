namespace DeepFlowTest.Utility.WpfUtility.Tree.Wrappers;

using System.Windows.Automation.Peers;

internal sealed class WpfAutomationPeerTargetObjectWrapper : TargetObjectWrapper
{
	public WpfAutomationPeerTargetObjectWrapper(AutomationPeer target)
		: base(target, CreateMetadata(target, TargetObjectKind.WpfAutomationPeer, "wpf", canReceiveActions: false))
	{
	}
}
