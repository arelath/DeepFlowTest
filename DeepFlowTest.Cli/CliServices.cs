namespace DeepFlowTest.Cli;

using System;

public sealed class CliServices
{
	public CliServices(
		CliDefaultsStore? defaultsStore = null,
		IProcessSnapshotSource? processSnapshotSource = null,
		ITargetResolver? targetResolver = null,
		IAutomationSessionService? appSessionService = null)
	{
		DefaultsStore = defaultsStore ?? new CliDefaultsStore();
		Automation = new AutomationServices(processSnapshotSource, targetResolver, appSessionService);
	}

	public CliDefaultsStore DefaultsStore { get; }

	public AutomationServices Automation { get; }

	public IProcessSnapshotSource ProcessSnapshotSource => Automation.ProcessSnapshotSource;

	public ITargetResolver TargetResolver => Automation.TargetResolver;

	public IAutomationSessionService AppSessionService => Automation.SessionService;
}
