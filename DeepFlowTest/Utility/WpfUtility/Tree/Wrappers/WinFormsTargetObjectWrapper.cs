namespace DeepFlowTest.Utility.WpfUtility.Tree.Wrappers;

using FormsControl = System.Windows.Forms.Control;

internal sealed class WinFormsTargetObjectWrapper : TargetObjectWrapper
{
	public WinFormsTargetObjectWrapper(FormsControl target)
		: base(target, CreateMetadata(
			target,
			TargetObjectKind.WinFormsControl,
			"winforms",
			canReceiveActions: true,
			target.IsHandleCreated ? target.Handle.ToInt64() : null))
	{
	}
}
