namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;

public static class CliRootCommand
{
	private static readonly HashSet<string> TargetBoundCommands = new(StringComparer.Ordinal)
	{
		"ping",
		"pipe status",
		"tree",
		"find",
		"node",
		"props",
		"selectors",
		"screenshot",
		"wait",
		"stream visual-tree",
		"stream visual-tree-delta",
		"stream screenshot",
		"stream event-log",
		"click",
		"focus",
		"type",
		"key",
		"set",
		"raise",
		"invoke",
	};

	private static readonly HashSet<string> TopLevelCommands = new(StringComparer.Ordinal)
	{
		"config",
		"processes",
		"ping",
		"pipe",
		"tree",
		"find",
		"node",
		"props",
		"selectors",
		"screenshot",
		"wait",
		"stream",
		"click",
		"focus",
		"type",
		"key",
		"set",
		"raise",
		"invoke",
		"version",
	};

	public static RootCommand Create()
	{
		var root = new RootCommand("Drive DeepFlowTest automation workflows.");
		root.SetAction(_ => 0);

		root.Add(CreateConfigCommand());
		root.Add(CreateProcessesCommand());
		root.Add(CreateTargetCommand("ping", "Ping a reusable target listener."));
		root.Add(CreatePipeCommand());
		root.Add(CreateTargetCommand("tree", "Read a target visual tree.", AddTreeOptions));
		root.Add(CreateTargetCommand("find", "Find nodes in a target.", AddFindOptions));
		root.Add(CreateTargetCommand("node", "Read one target node.", AddNodeContextOptions));
		root.Add(CreateTargetCommand("props", "Read node properties.", AddNodeContextOptions));
		root.Add(CreateTargetCommand("selectors", "Suggest selectors for a node.", AddTargetIdOption));
		root.Add(CreateTargetCommand("screenshot", "Capture a screenshot.", AddScreenshotOptions));
		root.Add(CreateTargetCommand("wait", "Wait for a node or state.", AddWaitOptions));
		root.Add(CreateStreamCommand());
		root.Add(CreateTargetCommand("click", "Click a target node.", AddClickOptions));
		root.Add(CreateTargetCommand("focus", "Focus a target node.", AddActionTargetOptions));
		root.Add(CreateTargetCommand("type", "Type text into a target node.", AddTypeOptions));
		root.Add(CreateTargetCommand("key", "Send key input.", AddKeyOptions));
		root.Add(CreateTargetCommand("set", "Set a property on a target node.", AddSetOptions));
		root.Add(CreateTargetCommand("raise", "Raise an event on a target node.", AddRaiseOptions));
		root.Add(CreateTargetCommand("invoke", "Invoke target-side code.", AddInvokeOptions));
		root.Add(CreateVersionCommand());

		return root;
	}

	public static string HelpText =>
		$"{DeepFlowTest.ProductInfo.Name} CLI{Environment.NewLine}"
		+ "Commands: config, processes, ping, pipe status, tree, find, node, props, selectors, screenshot, wait, stream, click, focus, type, key, set, raise, invoke, version";

	public static bool IsTargetBound(string commandPath) => TargetBoundCommands.Contains(commandPath);

	public static string GetCommandPath(IReadOnlyList<string> args)
	{
		var tokens = new List<string>();
		foreach (var arg in args)
		{
			if (arg.StartsWith("-", StringComparison.Ordinal))
				break;

			if (tokens.Count == 0 && !TopLevelCommands.Contains(arg))
				return arg;

			tokens.Add(arg);
			if (tokens.Count == 1 && arg is not ("config" or "pipe" or "stream"))
				break;

			if (tokens.Count == 2)
				break;
		}

		return string.Join(" ", tokens);
	}

	public static bool IsHelpRequest(IReadOnlyList<string> args) =>
		args.Count == 0 || args.Any(static x => x is "--help" or "-h" or "/?");

	private static Command CreateConfigCommand()
	{
		var config = new Command("config", "Read and edit CLI defaults.");
		config.SetAction(_ => 0);

		var get = new Command("get", "Get one default or all defaults.");
		var getKey = new Argument<string>("key") { Arity = ArgumentArity.ZeroOrOne, Description = "Default key." };
		get.Add(getKey);
		AddOutputOptions(get);
		get.SetAction(_ => 0);

		var set = new Command("set", "Set a default value.");
		set.Add(new Argument<string>("key") { Description = "Default key." });
		set.Add(new Argument<string>("value") { Description = "Default value." });
		AddOutputOptions(set);
		set.SetAction(_ => 0);

		var clear = new Command("clear", "Clear one default value.");
		clear.Add(new Argument<string>("key") { Description = "Default key." });
		AddOutputOptions(clear);
		clear.SetAction(_ => 0);

		var reset = new Command("reset", "Reset all CLI defaults.");
		AddOutputOptions(reset);
		reset.SetAction(_ => 0);

		config.Add(get);
		config.Add(set);
		config.Add(clear);
		config.Add(reset);
		return config;
	}

	private static Command CreateProcessesCommand()
	{
		var command = new Command("processes", "List candidate target processes without injection.");
		AddOutputOptions(command);
		command.Add(CreateOption<bool>("--candidates-only", "Only show likely WPF candidates."));
		command.SetAction(_ => 0);
		return command;
	}

	private static Command CreatePipeCommand()
	{
		var pipe = new Command("pipe", "Inspect reusable listener pipes.");
		pipe.SetAction(_ => 0);
		pipe.Add(CreateTargetCommand("status", "Read reusable listener status."));
		return pipe;
	}

	private static Command CreateStreamCommand()
	{
		var stream = new Command("stream", "Stream target data.");
		stream.SetAction(_ => 0);
		stream.Add(CreateTargetCommand("visual-tree", "Stream visual tree snapshots.", AddStreamOptions));
		stream.Add(CreateTargetCommand("visual-tree-delta", "Stream visual tree deltas.", AddStreamOptions));
		stream.Add(CreateTargetCommand("screenshot", "Stream screenshots.", AddStreamScreenshotOptions));
		stream.Add(CreateTargetCommand("event-log", "Stream target event logs.", AddStreamOptions));
		return stream;
	}

	private static Command CreateVersionCommand()
	{
		var version = new Command("version", "Print product version information.");
		AddOutputOptions(version);
		version.SetAction(_ => 0);
		return version;
	}

	private static Command CreateTargetCommand(string name, string description, Action<Command>? configure = null)
	{
		var command = new Command(name, description);
		AddTargetOptions(command);
		configure?.Invoke(command);
		command.SetAction(_ => 0);
		return command;
	}

	private static void AddTargetOptions(Command command)
	{
		command.Add(CreateOption<int?>("--pid", "Target process ID."));
		command.Add(CreateOption<string?>("--process", "Target process name."));
		command.Add(CreateOption<string?>("--window-title", "Substring of a top-level window title."));
		command.Add(CreateOption<int>("--timeout-ms", "Command timeout in milliseconds."));
		command.Add(CreateOption<bool>("--debug", "Write debug diagnostics to stderr."));
		command.Add(CreateOption<bool>("--no-inject", "Only connect to an existing listener."));
		command.Add(CreateOption<string?>("--pipe-id", "Custom reusable pipe ID."));
		command.Add(CreateOption<bool>("--allow-actions", "Allow target-mutating commands."));
		command.Add(CreateOption<bool>("--allow-arbitrary-invoke", "Allow arbitrary target-side invoke."));
		command.Add(CreateOption<string>("--after", "Snapshot after command: none, target, or tree."));
		AddOutputOptions(command);
	}

	private static void AddOutputOptions(Command command)
	{
		command.Add(CreateOption<string>("--format", "Output format: json or text."));
		command.Add(CreateOption<bool>("--pretty", "Pretty-print JSON output."));
		command.Add(CreateOption<bool>("--hide-empty", "Hide null and empty optional JSON fields."));
		command.Add(CreateOption<bool>("--use-short-ids", "Use short target IDs where possible."));
	}

	private static void AddTreeOptions(Command command)
	{
		command.Add(CreateOption<string>("--root", "Root target ID."));
		command.Add(CreateOption<string>("--target-id", "Root target ID."));
		command.Add(CreateOption<int>("--max-depth", "Maximum tree depth."));
		command.Add(CreateOption<int>("--limit", "Maximum node count."));
		command.Add(CreateOption<string>("--shape", "Tree shape: flat or nested."));
		command.Add(CreateOption<string>("--props", "Comma-separated property names."));
		command.Add(CreateOption<bool>("--include-hidden", "Include hidden nodes."));
		command.Add(CreateOption<bool>("--type-names", "Include type names."));
		command.Add(CreateOption<bool>("--include-path", "Include slash-style node paths."));
	}

	private static void AddFindOptions(Command command)
	{
		command.Add(CreateOption<string>("--name", "Name selector."));
		command.Add(CreateOption<string>("--automation-id", "Automation ID selector."));
		command.Add(CreateOption<string>("--text", "Text selector."));
		command.Add(CreateOption<string>("--type", "Type selector."));
		command.Add(CreateOption<string>("--type-contains", "Type contains selector."));
		command.Add(CreateOption<string>("--property", "Property equality selector as name=value."));
		command.Add(CreateOption<string>("--property-contains", "Property contains selector as name=value."));
		command.Add(CreateOption<string>("--property-regex", "Property regex selector as name=regex."));
		command.Add(CreateOption<bool>("--visible", "Require visible nodes."));
		command.Add(CreateOption<bool>("--enabled", "Require enabled nodes."));
		command.Add(CreateOption<bool>("--case-sensitive", "Use case-sensitive matching."));
		command.Add(CreateOption<int>("--limit", "Maximum match count."));
		command.Add(CreateOption<bool>("--require-match", "Fail when no matches are found."));
		command.Add(CreateOption<bool>("--include-path", "Include slash-style node paths."));
		command.Add(CreateOption<bool>("--include-properties", "Include selected properties."));
		command.Add(CreateOption<bool>("--include-children", "Include child context."));
		command.Add(CreateOption<bool>("--include-ancestors", "Include ancestor context."));
		command.Add(CreateOption<string>("--props", "Comma-separated property names."));
	}

	private static void AddTargetIdOption(Command command)
	{
		command.Add(CreateOption<string>("--target", "Target element ID."));
		command.Add(CreateOption<string>("--target-id", "Target element ID."));
	}

	private static void AddScreenshotOptions(Command command)
	{
		AddTargetIdOption(command);
		command.Add(CreateOption<string>("--image-format", "Image format."));
		command.Add(CreateOption<string>("--output", "Output image path."));
		command.Add(CreateOption<bool>("--base64", "Include base64 bytes in JSON output."));
	}

	private static void AddStreamOptions(Command command)
	{
		command.Add(CreateOption<int>("--duration-ms", "Stream duration in milliseconds."));
		command.Add(CreateOption<int>("--interval-ms", "Frame interval in milliseconds."));
		command.Add(CreateOption<string>("--target-id", "Target element ID."));
		command.Add(CreateOption<string>("--target", "Target element ID."));
		command.Add(CreateOption<string>("--props", "Comma-separated property names."));
	}

	private static void AddStreamScreenshotOptions(Command command)
	{
		AddStreamOptions(command);
		command.Add(CreateOption<string>("--image-format", "Image format."));
	}

	private static void AddWaitOptions(Command command)
	{
		AddFindOptions(command);
		command.Add(CreateOption<int>("--interval-ms", "Polling interval in milliseconds."));
		command.Add(CreateOption<int>("--match-count", "Required match count."));
		command.Add(CreateOption<bool>("--require-visible", "Require visible matches."));
		command.Add(CreateOption<bool>("--require-enabled", "Require enabled matches."));
	}

	private static void AddNodeContextOptions(Command command)
	{
		AddTargetIdOption(command);
		command.Add(CreateOption<string>("--props", "Comma-separated property names."));
		command.Add(CreateOption<bool>("--include-path", "Include slash-style node paths."));
		command.Add(CreateOption<bool>("--include-ancestors", "Include ancestor context."));
		command.Add(CreateOption<bool>("--include-children", "Include child context."));
		command.Add(CreateOption<bool>("--include-subtree", "Include subtree context."));
		command.Add(CreateOption<int>("--subtree-depth", "Bounded subtree depth."));
	}

	private static void AddClickOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateOption<string>("--button", "Mouse button."));
		command.Add(CreateOption<int>("--count", "Click count."));
	}

	private static void AddTypeOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateOption<string>("--text", "Text to type."));
		command.Add(CreateOption<bool>("--clear-first", "Clear existing text first."));
	}

	private static void AddKeyOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateOption<string>("--keys", "Keys to send."));
		command.Add(CreateOption<int>("--delay-ms", "Delay between keys."));
	}

	private static void AddSetOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateOption<string>("--property", "Property name."));
		command.Add(CreateOption<string>("--value", "Property value."));
	}

	private static void AddRaiseOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateOption<string>("--event", "Event name."));
	}

	private static void AddInvokeOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateOption<string>("--code", "Serialized invoke payload."));
		command.Add(CreateOption<string>("--operation", "Known operation name."));
	}

	private static void AddActionTargetOptions(Command command)
	{
		AddTargetIdOption(command);
		command.Add(CreateOption<string>("--name", "Name selector."));
		command.Add(CreateOption<string>("--automation-id", "Automation ID selector."));
		command.Add(CreateOption<string>("--text", "Text selector."));
		command.Add(CreateOption<string>("--type", "Type selector."));
		command.Add(CreateOption<string>("--type-contains", "Type contains selector."));
		command.Add(CreateOption<string>("--match-property", "Property equality selector as name=value."));
		command.Add(CreateOption<string>("--property-contains", "Property contains selector as name=value."));
		command.Add(CreateOption<string>("--property-regex", "Property regex selector as name=regex."));
		command.Add(CreateOption<bool>("--visible", "Require visible nodes."));
		command.Add(CreateOption<bool>("--enabled", "Require enabled nodes."));
		command.Add(CreateOption<bool>("--case-sensitive", "Use case-sensitive matching."));
		command.Add(CreateOption<bool>("--first", "Use the first matching node."));
		command.Add(CreateOption<int>("--index", "Use zero-based index from matching nodes."));
	}

	private static Option<T> CreateOption<T>(string name, string description) =>
		new(name)
		{
			Description = description,
		};
}
