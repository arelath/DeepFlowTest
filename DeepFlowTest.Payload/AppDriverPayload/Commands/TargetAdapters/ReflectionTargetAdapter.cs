namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

internal sealed class ReflectionTargetAdapter : UiTargetAdapterBase
{
	public override bool CanHandle(object target) => true;
}
