namespace DeepFlowTest.Automation;

public sealed class AutomationServices
{
	public AutomationServices(
		IProcessSnapshotSource? processSnapshotSource = null,
		ITargetResolver? targetResolver = null,
		IAutomationSessionService? sessionService = null)
	{
		ProcessSnapshotSource = processSnapshotSource ?? new LiveProcessSnapshotSource();
		TargetResolver = targetResolver ?? new TargetResolver(ProcessSnapshotSource);
		SessionService = sessionService ?? new AutomationSessionService();
	}

	public IProcessSnapshotSource ProcessSnapshotSource { get; }

	public ITargetResolver TargetResolver { get; }

	public IAutomationSessionService SessionService { get; }
}
