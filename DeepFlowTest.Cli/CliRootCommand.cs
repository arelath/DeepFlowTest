namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using DeepFlowTest.Contracts;

public static class CliRootCommand
{
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
		"record",
		"click",
		"wheel",
		"drag",
		"focus",
		"type",
		"key",
		"set",
		"raise",
		"invoke",
		"version",
	};

	public static RootCommand Create() => Create(actions: null);

	internal static RootCommand Create(CliCommandActions? actions)
	{
		var root = new RootCommand("Drive DeepFlowTest automation workflows.");
		SetAction(root, actions?.Root);
		AddTargetOptions(root, recursive: true);

		root.Add(CreateConfigCommand(actions));
		root.Add(CreateProcessesCommand(actions));
		root.Add(CreateTargetCommand("ping", "Ping a reusable target listener.", actions, static actionSet => actionSet.Ping));
		root.Add(CreatePipeCommand(actions));
		root.Add(CreateTargetCommand("tree", "Read a target visual tree.", actions, static actionSet => actionSet.Tree, AddTreeOptions));
		root.Add(CreateTargetCommand("find", "Find nodes in a target.", actions, static actionSet => actionSet.Find, AddFindOptions));
		root.Add(CreateTargetCommand("node", "Read one target node.", actions, static actionSet => actionSet.Node, AddNodeContextOptions));
		root.Add(CreateTargetCommand("props", "Read node properties.", actions, static actionSet => actionSet.Props, AddNodeContextOptions));
		root.Add(CreateTargetCommand("selectors", "Suggest selectors for a node.", actions, static actionSet => actionSet.Selectors, AddTargetIdOption));
		root.Add(CreateTargetCommand("screenshot", "Capture a screenshot.", actions, static actionSet => actionSet.Screenshot, AddScreenshotOptions));
		root.Add(CreateTargetCommand("wait", "Wait for a node or state.", actions, static actionSet => actionSet.Wait, AddWaitOptions));
		root.Add(CreateStreamCommand(actions));
		root.Add(CreateRecordCommand(actions));
		root.Add(CreateTargetCommand("click", "Click a target node.", actions, static actionSet => actionSet.Click, AddClickOptions));
		root.Add(CreateTargetCommand("wheel", "Send mouse-wheel input to a target node.", actions, static actionSet => actionSet.Wheel, AddMouseWheelOptions));
		root.Add(CreateTargetCommand("drag", "Drag a source node and drop it on a destination node.", actions, static actionSet => actionSet.Drag, AddDragOptions));
		root.Add(CreateTargetCommand("focus", "Focus a target node.", actions, static actionSet => actionSet.Focus, AddActionTargetOptions));
		root.Add(CreateTargetCommand("type", "Type text into a target node.", actions, static actionSet => actionSet.Type, AddTypeOptions));
		root.Add(CreateTargetCommand("key", "Send key input.", actions, static actionSet => actionSet.Key, AddKeyOptions));
		root.Add(CreateTargetCommand("set", "Set a property on a target node.", actions, static actionSet => actionSet.Set, AddSetOptions));
		root.Add(CreateTargetCommand("raise", "Raise an event on a target node.", actions, static actionSet => actionSet.Raise, AddRaiseOptions));
		root.Add(CreateTargetCommand("invoke", "Invoke target-side code.", actions, static actionSet => actionSet.Invoke, AddInvokeOptions));
		root.Add(CreateVersionCommand(actions));

		return root;
	}

	public static string HelpText =>
		$"{DeepFlowTest.ProductInfo.Name} CLI{Environment.NewLine}"
		+ "Commands: config, processes, ping, pipe status, tree, find, node, props, selectors, screenshot, wait, stream, record, click, wheel, drag, focus, type, key, set, raise, invoke, version";

	public static string GetCommandPath(IReadOnlyList<string> args)
	{
		List<string> tokens = [];
		for (var index = 0; index < args.Count; index++)
		{
			var arg = args[index];
			if (arg.StartsWith("-", StringComparison.Ordinal))
			{
				var optionName = arg.Split('=', 2)[0];
				if (!arg.Contains('=', StringComparison.Ordinal) && OptionHasSeparateValue(optionName) && index + 1 < args.Count)
					index++;
				continue;
			}

			if (tokens.Count == 0 && !TopLevelCommands.Contains(arg))
				return arg;

			tokens.Add(arg);
			if (tokens.Count == 1 && arg is not ("config" or "pipe" or "stream" or "record"))
				break;

			if (tokens.Count == 2)
				break;
		}

		return string.Join(" ", tokens);
	}

	public static bool IsHelpRequest(IReadOnlyList<string> args) =>
		args.Count == 0 || args.Any(static x => x is "--help" or "-h" or "/?");

	private static Command CreateConfigCommand(CliCommandActions? actions)
	{
		var config = new Command("config", "Read and edit CLI defaults.");
		SetAction(config, actions?.Config);

		var get = new Command("get", "Get one default or all defaults.");
		var getKey = new Argument<string>("key") { Arity = ArgumentArity.ZeroOrOne, Description = "Default key." };
		get.Add(getKey);
		SetAction(get, actions?.ConfigGet);

		var set = new Command("set", "Set a default value.");
		set.Add(new Argument<string>("key") { Description = "Default key." });
		set.Add(new Argument<string>("value") { Description = "Default value." });
		set.Add(CreateOption<bool>("--json", "Parse the value as JSON."));
		SetAction(set, actions?.ConfigSet);

		var clear = new Command("clear", "Clear one default value.");
		clear.Add(new Argument<string>("key") { Description = "Default key." });
		SetAction(clear, actions?.ConfigClear);

		var reset = new Command("reset", "Reset all CLI defaults.");
		reset.Add(CreateOption<bool>("--yes", "Confirm the reset."));
		SetAction(reset, actions?.ConfigReset);

		config.Add(get);
		config.Add(set);
		config.Add(clear);
		config.Add(reset);
		return config;
	}

	private static Command CreateProcessesCommand(CliCommandActions? actions)
	{
		var command = new Command("processes", "List candidate target processes without injection.");
		command.Add(CreateOption<bool>("--candidates-only", "Only show likely WPF candidates."));
		command.Add(CreateOption<bool>("--show-all", "Show all processes. This is the default and is accepted for compatibility."));
		SetAction(command, actions?.Processes);
		return command;
	}

	private static Command CreatePipeCommand(CliCommandActions? actions)
	{
		var pipe = new Command("pipe", "Inspect reusable listener pipes.");
		SetAction(pipe, actions?.Pipe);
		pipe.Add(CreateTargetCommand("status", "Read reusable listener status.", actions, static actionSet => actionSet.PipeStatus));
		return pipe;
	}

	private static Command CreateStreamCommand(CliCommandActions? actions)
	{
		var stream = new Command("stream", "Stream target data.");
		SetAction(stream, actions?.Stream);
		stream.Add(CreateTargetCommand("visual-tree", "Stream visual tree snapshots.", actions, static actionSet => actionSet.StreamVisualTree, AddStreamOptions));
		stream.Add(CreateTargetCommand("visual-tree-delta", "Stream visual tree deltas.", actions, static actionSet => actionSet.StreamVisualTreeDelta, AddStreamOptions));
		stream.Add(CreateTargetCommand("screenshot", "Stream screenshots.", actions, static actionSet => actionSet.StreamScreenshot, AddStreamScreenshotOptions));
		stream.Add(CreateTargetCommand("event-log", "Stream target event logs.", actions, static actionSet => actionSet.StreamEventLog, AddStreamOptions));
		stream.Add(CreateTargetCommand("binding-failures", "Stream WPF binding failures.", actions, static actionSet => actionSet.StreamBindingFailures, AddStreamOptions));
		stream.Add(CreateTargetCommand("semantic-recording", "Stream semantic recording batches.", actions, static actionSet => actionSet.StreamSemanticRecording, AddSemanticRecordingStreamOptions));
		return stream;
	}

	private static Command CreateRecordCommand(CliCommandActions? actions)
	{
		var record = new Command("record", "Record target data.");
		SetAction(record, actions?.Record);
		record.Add(CreateTargetCommand("semantic", "Record semantic UI actions and visual tree changes to JSON.", actions, static actionSet => actionSet.RecordSemantic, AddRecordSemanticOptions));
		return record;
	}

	private static Command CreateVersionCommand(CliCommandActions? actions)
	{
		var version = new Command("version", "Print product version information.");
		SetAction(version, actions?.Version);
		return version;
	}

	private static Command CreateTargetCommand(
		string name,
		string description,
		CliCommandActions? actions,
		Func<CliCommandActions, Func<int>> selectAction,
		Action<Command>? configure = null)
	{
		var command = new Command(name, description);
		configure?.Invoke(command);
		SetAction(command, actions is null ? null : selectAction(actions));
		return command;
	}

	private static void SetAction(Command command, Func<int>? action)
	{
		command.SetAction(_ => action?.Invoke() ?? 0);
	}

	private static void AddTargetOptions(Command command, bool recursive = false)
	{
		command.Add(CreateOption<int?>("--pid", "Target process ID.", recursive: recursive));
		command.Add(CreateOption<string?>("--process", "Target process name.", recursive: recursive));
		command.Add(CreateOption<string?>("--window-title", "Substring of a top-level window title.", recursive: recursive));
		command.Add(CreateOption<int>("--timeout-ms", "Command timeout in milliseconds.", recursive: recursive));
		command.Add(CreateOption<bool>("--debug", "Write debug diagnostics to stderr.", recursive: recursive));
		command.Add(CreateOption<bool>("--no-inject", "Only connect to an existing listener.", recursive: recursive));
		command.Add(CreateOption<string?>("--pipe-id", "Custom reusable pipe ID.", recursive: recursive));
		command.Add(CreateOption<bool>("--allow-actions", "Allow target-mutating commands.", recursive: recursive));
		command.Add(CreateOption<bool>("--allow-arbitrary-invoke", "Allow arbitrary target-side invoke.", recursive: recursive));
		command.Add(CreateOption<string>("--after", "Snapshot after command: none, target, or tree.", recursive: recursive));
		AddOutputOptions(command, recursive);
	}

	private static void AddOutputOptions(Command command, bool recursive = false)
	{
		command.Add(CreateOption<string>("--format", "Output format: json or text.", recursive: recursive));
		command.Add(CreateOption<bool>("--pretty", "Pretty-print JSON output.", recursive: recursive));
		command.Add(CreateOption<bool>("--hide-empty", "Hide null and empty optional JSON fields.", recursive: recursive));
		command.Add(CreateOption<bool>("--use-short-ids", "Use short target IDs where possible.", recursive: recursive));
	}

	private static void AddTreeOptions(Command command)
	{
		command.Add(CreateOption<string>("--root", "Root target ID."));
		command.Add(CreateOption<string>("--target-id", "Root target ID."));
		command.Add(CreateOption<int>("--max-depth", "Maximum tree depth."));
		command.Add(CreateOption<int>("--limit", "Maximum node count."));
		command.Add(CreateParsedOption<TreeShape>("--shape", "Tree shape: flat or nested.", CliValueParser.ParseTreeShape));
		command.Add(CreateOption<string>("--props", "Comma-separated property names."));
		command.Add(CreateOption<bool>("--include-hidden", "Include hidden nodes."));
		command.Add(CreateOption<string>("--type-names", "Comma-separated type names to include."));
		command.Add(CreateOption<bool>("--include-path", "Include slash-style node paths."));
	}

	private static void AddFindOptions(Command command)
	{
		command.Add(CreateOption<string>("--name", "Name selector."));
		command.Add(CreateOption<string>("--automation-id", "Automation ID selector."));
		command.Add(CreateOption<string>("--text", "Text selector."));
		command.Add(CreateOption<string>("--type", "Type selector."));
		command.Add(CreateOption<string>("--type-contains", "Type contains selector."));
		command.Add(CreateOption<string>("--property", "Property equality selector as name=value.", "--prop"));
		command.Add(CreateOption<string>("--property-contains", "Property contains selector as name=value.", "--contains"));
		command.Add(CreateOption<string>("--property-regex", "Property regex selector as name=regex.", "--regex"));
		command.Add(CreateOption<bool>("--visible", "Require visible nodes."));
		command.Add(CreateOption<bool>("--enabled", "Require enabled nodes."));
		command.Add(CreateOption<bool>("--case-sensitive", "Use case-sensitive matching."));
		command.Add(CreateOption<int>("--limit", "Maximum match count."));
		command.Add(CreateOption<bool>("--require-match", "Fail when no matches are found."));
		command.Add(CreateOption<bool>("--include-path", "Include slash-style node paths."));
		command.Add(CreateOption<bool>("--include-properties", "Include selected properties."));
		command.Add(CreateOption<bool>("--include-children", "Include child context."));
		command.Add(CreateOption<bool>("--include-ancestors", "Include ancestor context."));
		command.Add(CreateOption<string>("--include", "Comma-separated optional sections: path, properties, children, ancestors."));
		command.Add(CreateOption<string>("--props", "Comma-separated property names."));
	}

	private static void AddTargetIdOption(Command command)
	{
		command.Add(CreateOption<string>("--target", "Target element ID."));
		command.Add(CreateOption<string>("--target-id", "Target element ID."));
	}

	private static void AddScreenshotOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateParsedOption<ImageFormat>("--image-format", "Image format.", CliValueParser.ParseImageFormat));
		command.Add(CreateOption<string>("--output", "Output image path.", "--out"));
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
		command.Add(CreateParsedOption<ImageFormat>("--image-format", "Image format.", CliValueParser.ParseImageFormat));
	}

	private static void AddSemanticRecordingStreamOptions(Command command)
	{
		AddStreamOptions(command);
		command.Add(CreateOption<int>("--text-idle-ms", "Text coalescing idle threshold in milliseconds."));
		command.Add(CreateOption<int>("--max-queued-actions", "Maximum queued recording actions."));
		command.Add(CreateOption<int>("--max-batch-frames", "Maximum recording frames per batch."));
		command.Add(CreateOption<int>("--limit", "Maximum visual tree nodes per snapshot."));
	}

	private static void AddRecordSemanticOptions(Command command)
	{
		AddSemanticRecordingStreamOptions(command);
		command.Add(CreateOption<string>("--output", "Recording output path.", "--out"));
		command.Add(CreateOption<string>("--recording-format", "Recording file format: condensed-agent, condensed-diagnostic, compact-json, or raw-json."));
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
		command.Add(CreateOption<bool>("--include-ancestors", "Include ancestor context.", "--ancestors"));
		command.Add(CreateOption<bool>("--include-children", "Include child context.", "--children"));
		command.Add(CreateOption<bool>("--include-subtree", "Include subtree context.", "--subtree"));
		command.Add(CreateOption<int>("--subtree-depth", "Bounded subtree depth."));
	}

	private static void AddClickOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateParsedOption<CliClickButton>("--button", "Mouse button.", CliValueParser.ParseClickButton));
		command.Add(CreateOption<int>("--count", "Click count."));
		command.Add(CreateOption<bool>("--double", "Send a double-click routed event."));
	}

	private static void AddMouseWheelOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateOption<int>("--delta", "Signed wheel delta; positive scrolls up and negative scrolls down."));
	}

	private static void AddDragOptions(Command command)
	{
		AddActionTargetOptions(command);
		AddDestinationTargetOptions(command);
		command.Add(CreateOption<int>("--duration-ms", "Mouse movement duration in milliseconds."));
		command.Add(CreateOption<int>("--hold-ms", "Delay after mouse down before movement."));
		command.Add(CreateOption<int>("--step-interval-ms", "Delay between movement steps."));
		command.Add(CreateOption<int>("--post-drop-wait-ms", "Delay after mouse up."));
		command.Add(CreateOption<double>("--source-anchor-x", "Normalized source X anchor."));
		command.Add(CreateOption<double>("--source-anchor-y", "Normalized source Y anchor."));
		command.Add(CreateOption<double>("--destination-anchor-x", "Normalized destination X anchor."));
		command.Add(CreateOption<double>("--destination-anchor-y", "Normalized destination Y anchor."));
		command.Add(CreateOption<bool>("--injected-events", "Use framework-level injected mouse events for WPF targets."));
		command.Add(CreateOption<bool>("--foreground", "Legacy compatibility option; drag actions run against the target window directly."));
		command.Add(CreateOption<bool>("--validate-same-process", "Require drag points to remain over the target process."));
	}

	private static void AddTypeOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateOption<string>("--value", "Text to type."));
		command.Add(CreateOption<string>("--selector-text", "Text selector."));
		command.Add(CreateOption<bool>("--clear-first", "Clear existing text first."));
	}

	private static void AddKeyOptions(Command command)
	{
		AddActionTargetOptions(command);
		command.Add(CreateOption<string>("--keys", "Keys to send."));
		command.Add(CreateOption<bool>("--foreground", "Bring the target main window to foreground first."));
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
		command.Add(CreateOption<string>("--match-property", "Property equality selector as name=value.", "--prop"));
		command.Add(CreateOption<string>("--property-contains", "Property contains selector as name=value.", "--contains"));
		command.Add(CreateOption<string>("--property-regex", "Property regex selector as name=regex.", "--regex"));
		command.Add(CreateOption<bool>("--visible", "Require visible nodes.", "--require-visible"));
		command.Add(CreateOption<bool>("--enabled", "Require enabled nodes.", "--require-enabled"));
		command.Add(CreateOption<bool>("--case-sensitive", "Use case-sensitive matching."));
		command.Add(CreateOption<bool>("--first", "Use the first matching node."));
		command.Add(CreateOption<int>("--index", "Use zero-based index from matching nodes."));
	}

	private static void AddDestinationTargetOptions(Command command)
	{
		command.Add(CreateOption<string>("--to-target", "Destination target element ID.", "--to-target-id"));
		command.Add(CreateOption<string>("--to-name", "Destination name selector."));
		command.Add(CreateOption<string>("--to-automation-id", "Destination automation ID selector."));
		command.Add(CreateOption<string>("--to-text", "Destination text selector."));
		command.Add(CreateOption<string>("--to-type", "Destination type selector."));
		command.Add(CreateOption<string>("--to-type-contains", "Destination type contains selector."));
		command.Add(CreateOption<string>("--to-match-property", "Destination property equality selector as name=value.", "--to-prop", "--to-property"));
		command.Add(CreateOption<string>("--to-property-contains", "Destination property contains selector as name=value.", "--to-contains"));
		command.Add(CreateOption<string>("--to-property-regex", "Destination property regex selector as name=regex.", "--to-regex"));
		command.Add(CreateOption<bool>("--to-visible", "Require visible destination nodes.", "--to-require-visible"));
		command.Add(CreateOption<bool>("--to-enabled", "Require enabled destination nodes.", "--to-require-enabled"));
		command.Add(CreateOption<bool>("--to-case-sensitive", "Use case-sensitive destination matching."));
		command.Add(CreateOption<bool>("--to-first", "Use the first matching destination node."));
		command.Add(CreateOption<int>("--to-index", "Use zero-based destination index from matching nodes."));
	}

	private static Option<T> CreateOption<T>(string name, string description, params string[] aliases) =>
		CreateOption<T>(name, description, recursive: false, aliases);

	private static Option<T> CreateOption<T>(string name, string description, bool recursive, params string[] aliases) =>
		ConfigureOption(new Option<T>(name, aliases)
		{
			Description = description,
			Recursive = recursive,
		}, name);

	private static Option<T> CreateParsedOption<T>(string name, string description, Func<string?, T> parse, params string[] aliases)
	{
		var option = new Option<T>(name, aliases)
		{
			Description = description,
			Arity = ArgumentArity.ExactlyOne,
		};
		option.CustomParser = result =>
		{
			var value = result.Tokens.Count == 0 ? null : result.Tokens[0].Value;
			try
			{
				return parse(value);
			}
			catch (AutomationException ex)
			{
				result.AddError(ex.Message);
				return default!;
			}
		};
		return option;
	}

	private static Option<T> ConfigureOption<T>(Option<T> option, string name)
	{
		if (typeof(T) == typeof(bool))
		{
			option.Arity = ArgumentArity.ZeroOrOne;
			option.CustomParser = result =>
			{
				if (result.Tokens.Count == 0)
					return (T)(object)true;
				if (bool.TryParse(result.Tokens[0].Value, out var value))
					return (T)(object)value;

				result.AddError($"Option `{name}` expects `true` or `false`.");
				return default!;
			};
		}

		return option;
	}

	private static bool OptionHasSeparateValue(string optionName) =>
		optionName is "--pid"
			or "--process"
			or "--window-title"
			or "--timeout-ms"
			or "--pipe-id"
			or "--after"
			or "--format"
			or "--root"
			or "--target-id"
			or "--target"
			or "--max-depth"
			or "--limit"
			or "--include"
			or "--shape"
			or "--props"
			or "--type-names"
			or "--name"
			or "--automation-id"
			or "--text"
			or "--type"
			or "--type-contains"
			or "--property"
			or "--prop"
			or "--property-contains"
			or "--contains"
			or "--property-regex"
			or "--regex"
			or "--image-format"
			or "--output"
			or "--out"
			or "--duration-ms"
			or "--interval-ms"
			or "--text-idle-ms"
			or "--max-queued-actions"
			or "--max-batch-frames"
			or "--recording-format"
			or "--match-count"
			or "--subtree-depth"
			or "--button"
			or "--count"
			or "--delta"
			or "--hold-ms"
			or "--step-interval-ms"
			or "--post-drop-wait-ms"
			or "--source-anchor-x"
			or "--source-anchor-y"
			or "--destination-anchor-x"
			or "--destination-anchor-y"
			or "--injected-events"
			or "--to-target"
			or "--to-target-id"
			or "--to-name"
			or "--to-automation-id"
			or "--to-text"
			or "--to-type"
			or "--to-type-contains"
			or "--to-match-property"
			or "--to-property"
			or "--to-prop"
			or "--to-property-contains"
			or "--to-contains"
			or "--to-property-regex"
			or "--to-regex"
			or "--to-index"
			or "--value"
			or "--selector-text"
			or "--keys"
			or "--delay-ms"
			or "--event"
			or "--code"
			or "--operation";
}
