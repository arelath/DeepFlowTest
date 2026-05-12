namespace DeepFlowTest.Cli;

using System;

public sealed class CliServices
{
	public CliServices(
		CliDefaultsStore? defaultsStore = null,
		IProcessSnapshotSource? processSnapshotSource = null,
		ITargetResolver? targetResolver = null,
		ICliAppSessionService? appSessionService = null)
	{
		DefaultsStore = defaultsStore ?? new CliDefaultsStore();
		ProcessSnapshotSource = processSnapshotSource ?? new LiveProcessSnapshotSource();
		TargetResolver = targetResolver ?? new TargetResolver(ProcessSnapshotSource);
		AppSessionService = appSessionService ?? new CliAppSessionService();
	}

	public CliDefaultsStore DefaultsStore { get; }

	public IProcessSnapshotSource ProcessSnapshotSource { get; }

	public ITargetResolver TargetResolver { get; }

	public ICliAppSessionService AppSessionService { get; }
}
