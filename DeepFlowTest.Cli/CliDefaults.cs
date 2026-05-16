namespace DeepFlowTest.Cli;

using System.Collections.Generic;
using System.Text.Json.Serialization;
using DeepFlowTest.Contracts;

public sealed class CliDefaults
{
	public int SchemaVersion { get; set; } = 1;

	public CliCommonDefaults Common { get; set; } = new();

	public CliCommandDefaults Commands { get; set; } = new();

	[JsonIgnore]
	public int TimeoutMs { get => Common.TimeoutMs; set => Common.TimeoutMs = value; }

	[JsonIgnore]
	public string OutputFormat { get => Common.Format; set => Common.Format = value; }

	[JsonIgnore]
	public bool HideEmpty { get => Common.HideEmpty; set => Common.HideEmpty = value; }

	[JsonIgnore]
	public bool UseShortIds { get => Common.UseShortIds; set => Common.UseShortIds = value; }

	[JsonIgnore]
	public string AfterSnapshot { get => Common.After; set => Common.After = value; }

	[JsonIgnore]
	public string TreeShape { get => ProtocolValueMapper.FormatTreeShape(Commands.Tree.Shape); set => Commands.Tree.Shape = ProtocolValueMapper.ParseTreeShape(value); }

	[JsonIgnore]
	public int TreeMaxDepth { get => Commands.Tree.MaxDepth; set => Commands.Tree.MaxDepth = value; }

	[JsonIgnore]
	public int TreeLimit { get => Commands.Tree.Limit; set => Commands.Tree.Limit = value; }

	[JsonIgnore]
	public List<string> PropertyNames { get => Commands.Tree.Props; set => Commands.Tree.Props = value; }

	[JsonIgnore]
	public int FindLimit { get => Commands.Find.Limit; set => Commands.Find.Limit = value; }

	[JsonIgnore]
	public int WaitIntervalMs { get => Commands.Wait.IntervalMs; set => Commands.Wait.IntervalMs = value; }

	[JsonIgnore]
	public int WaitMatchCount { get => Commands.Wait.MatchCount; set => Commands.Wait.MatchCount = value; }

	[JsonIgnore]
	public int StreamDurationMs { get => Commands.Stream.DurationMs; set => Commands.Stream.DurationMs = value; }

	[JsonIgnore]
	public int StreamIntervalMs { get => Commands.Stream.IntervalMs; set => Commands.Stream.IntervalMs = value; }

	[JsonIgnore]
	public string ScreenshotFormat { get => Commands.Screenshot.ImageFormat.ToProtocolString(); set => Commands.Screenshot.ImageFormat = ImageFormatExtensions.ParseProtocolString(value); }

	[JsonIgnore]
	public int KeyDelayMs { get => Commands.Key.DelayMs; set => Commands.Key.DelayMs = value; }

	[JsonIgnore]
	public bool EnsureForeground { get => Commands.Key.Foreground; set => Commands.Key.Foreground = value; }

	public static List<string> CreateDefaultPropertyList() =>
		new(KnownProperties.DefaultVisualTreePropertyNames);
}

public sealed class CliCommonDefaults
{
	public int? Pid { get; set; }
	public string? Process { get; set; }
	public string? WindowTitle { get; set; }
	public int TimeoutMs { get; set; } = TimeoutDefaults.CliCommandTimeoutMs;
	public string Format { get; set; } = "json";
	public bool Pretty { get; set; }
	public bool HideEmpty { get; set; } = true;
	public bool UseShortIds { get; set; } = true;
	public bool Debug { get; set; }
	public bool NoInject { get; set; }
	public string? PipeId { get; set; }
	public bool AllowActions { get; set; }
	public bool AllowArbitraryInvoke { get; set; }
	public string After { get; set; } = "none";
}

public sealed class CliCommandDefaults
{
	public CliProcessesDefaults Processes { get; set; } = new();
	public CliTreeDefaults Tree { get; set; } = new();
	public CliFindDefaults Find { get; set; } = new();
	public CliNodeDefaults Node { get; set; } = new();
	public CliPropsDefaults Props { get; set; } = new();
	public CliSelectorsDefaults Selectors { get; set; } = new();
	public CliScreenshotDefaults Screenshot { get; set; } = new();
	public CliWaitDefaults Wait { get; set; } = new();
	public CliStreamDefaults Stream { get; set; } = new();
	public CliClickDefaults Click { get; set; } = new();
	public CliFocusDefaults Focus { get; set; } = new();
	public CliTypeDefaults Type { get; set; } = new();
	public CliKeyDefaults Key { get; set; } = new();
	public CliSetDefaults Set { get; set; } = new();
	public CliRaiseDefaults Raise { get; set; } = new();
	public CliInvokeDefaults Invoke { get; set; } = new();
	public CliPipeDefaults Pipe { get; set; } = new();
}

public sealed class CliProcessesDefaults
{
	public string? Name { get; set; }
	public bool ShowAll { get; set; }
}

public sealed class CliTreeDefaults
{
	public TreeShape Shape { get; set; } = TreeShape.Flat;
	public string? Root { get; set; }
	public int MaxDepth { get; set; } = -1;
	public int Limit { get; set; } = 1_000;
	public List<string> Props { get; set; } = CliDefaults.CreateDefaultPropertyList();
	public List<string>? TypeNames { get; set; }
	public bool IncludePath { get; set; }
	public bool IncludeHidden { get; set; }
}

public sealed class CliFindDefaults
{
	public string? Type { get; set; }
	public string? TypeContains { get; set; }
	public string? Name { get; set; }
	public string? AutomationId { get; set; }
	public string? Text { get; set; }
	public string? PropertyEquals { get; set; }
	public string? PropertyContains { get; set; }
	public string? PropertyRegex { get; set; }
	public bool Visible { get; set; }
	public bool Enabled { get; set; }
	public bool CaseSensitive { get; set; }
	public int Limit { get; set; } = 50;
	public List<string> Include { get; set; } = ["path", "properties"];
	public bool RequireMatch { get; set; }
}

public sealed class CliNodeDefaults
{
	public string? TargetId { get; set; }
	public string Props { get; set; } = "default";
	public bool Ancestors { get; set; }
	public bool Children { get; set; }
	public bool Subtree { get; set; }
	public int? SubtreeDepth { get; set; }
}

public sealed class CliPropsDefaults
{
	public string? TargetId { get; set; }
	public List<string> Props { get; set; } = CliDefaults.CreateDefaultPropertyList();
}

public sealed class CliSelectorsDefaults
{
	public string? TargetId { get; set; }
}

public sealed class CliScreenshotDefaults
{
	public string? TargetId { get; set; }
	public ImageFormat ImageFormat { get; set; } = ImageFormat.Png;
	public string? OutputPath { get; set; }
	public bool Base64 { get; set; }
}

public sealed class CliWaitDefaults
{
	public string? Type { get; set; }
	public string? TypeContains { get; set; }
	public string? Name { get; set; }
	public string? AutomationId { get; set; }
	public string? Text { get; set; }
	public string? PropertyEquals { get; set; }
	public string? PropertyContains { get; set; }
	public string? PropertyRegex { get; set; }
	public bool Visible { get; set; }
	public bool Enabled { get; set; }
	public bool CaseSensitive { get; set; }
	public int IntervalMs { get; set; } = TimeoutDefaults.CliWaitIntervalMs;
	public int MatchCount { get; set; } = 1;
	public bool RequireMatch { get; set; }
}

public sealed class CliStreamDefaults
{
	public int DurationMs { get; set; }
	public int IntervalMs { get; set; } = TimeoutDefaults.StreamIntervalMs;
	public List<string> Props { get; set; } = CliDefaults.CreateDefaultPropertyList();
	public string? TargetId { get; set; }
	public ImageFormat ImageFormat { get; set; } = ImageFormat.Png;
}

public sealed class CliElementSelectorDefaults
{
	public string? TargetId { get; set; }
	public string? Type { get; set; }
	public string? TypeContains { get; set; }
	public string? Name { get; set; }
	public string? AutomationId { get; set; }
	public string? Text { get; set; }
	public string? PropertyEquals { get; set; }
	public string? PropertyContains { get; set; }
	public string? PropertyRegex { get; set; }
	public bool CaseSensitive { get; set; }
	public bool First { get; set; }
	public int? Index { get; set; }
	public bool RequireVisible { get; set; }
	public bool RequireEnabled { get; set; }
}

public sealed class CliElementSelectorWithoutTextDefaults
{
	public string? TargetId { get; set; }
	public string? Type { get; set; }
	public string? TypeContains { get; set; }
	public string? Name { get; set; }
	public string? AutomationId { get; set; }
	public string? PropertyEquals { get; set; }
	public string? PropertyContains { get; set; }
	public string? PropertyRegex { get; set; }
	public bool CaseSensitive { get; set; }
	public bool First { get; set; }
	public int? Index { get; set; }
	public bool RequireVisible { get; set; }
	public bool RequireEnabled { get; set; }
}

public sealed class CliClickDefaults
{
	public CliElementSelectorDefaults Selector { get; set; } = new();
	public MouseButtonKind Button { get; set; } = MouseButtonKind.Left;
	public bool Double { get; set; }
}

public sealed class CliFocusDefaults
{
	public CliElementSelectorDefaults Selector { get; set; } = new();
}

public sealed class CliTypeDefaults
{
	public CliElementSelectorWithoutTextDefaults Selector { get; set; } = new();
	public string? Text { get; set; }
	public bool ClearFirst { get; set; }
}

public sealed class CliKeyDefaults
{
	public string? Keys { get; set; }
	public bool Foreground { get; set; } = true;
	public int DelayMs { get; set; } = TimeoutDefaults.KeyboardDelayMs;
}

public sealed class CliSetDefaults
{
	public CliElementSelectorDefaults Selector { get; set; } = new();
	public string? Property { get; set; }
	public string? Value { get; set; }
}

public sealed class CliRaiseDefaults
{
	public CliElementSelectorDefaults Selector { get; set; } = new();
	public string? Event { get; set; }
}

public sealed class CliInvokeDefaults
{
	public CliElementSelectorDefaults Selector { get; set; } = new();
	public string? Operation { get; set; }
	public string? Code { get; set; }
}

public sealed class CliPipeDefaults
{
	public CliPipeStatusDefaults Status { get; set; } = new();
}

public sealed class CliPipeStatusDefaults
{
}
