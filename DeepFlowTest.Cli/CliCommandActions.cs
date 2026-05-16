namespace DeepFlowTest.Cli;

using System;

internal sealed class CliCommandActions
{
	public Func<int> Root { get; init; } = NoAction;

	public Func<int> Config { get; init; } = NoAction;

	public Func<int> ConfigGet { get; init; } = NoAction;

	public Func<int> ConfigSet { get; init; } = NoAction;

	public Func<int> ConfigClear { get; init; } = NoAction;

	public Func<int> ConfigReset { get; init; } = NoAction;

	public Func<int> Processes { get; init; } = NoAction;

	public Func<int> Ping { get; init; } = NoAction;

	public Func<int> Pipe { get; init; } = NoAction;

	public Func<int> PipeStatus { get; init; } = NoAction;

	public Func<int> Tree { get; init; } = NoAction;

	public Func<int> Find { get; init; } = NoAction;

	public Func<int> Node { get; init; } = NoAction;

	public Func<int> Props { get; init; } = NoAction;

	public Func<int> Selectors { get; init; } = NoAction;

	public Func<int> Screenshot { get; init; } = NoAction;

	public Func<int> Wait { get; init; } = NoAction;

	public Func<int> Stream { get; init; } = NoAction;

	public Func<int> StreamVisualTree { get; init; } = NoAction;

	public Func<int> StreamVisualTreeDelta { get; init; } = NoAction;

	public Func<int> StreamScreenshot { get; init; } = NoAction;

	public Func<int> StreamEventLog { get; init; } = NoAction;

	public Func<int> StreamBindingFailures { get; init; } = NoAction;

	public Func<int> Click { get; init; } = NoAction;

	public Func<int> Focus { get; init; } = NoAction;

	public Func<int> Type { get; init; } = NoAction;

	public Func<int> Key { get; init; } = NoAction;

	public Func<int> Set { get; init; } = NoAction;

	public Func<int> Raise { get; init; } = NoAction;

	public Func<int> Invoke { get; init; } = NoAction;

	public Func<int> Version { get; init; } = NoAction;

	private static int NoAction() => 0;
}
