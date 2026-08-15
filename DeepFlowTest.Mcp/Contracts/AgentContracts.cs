namespace DeepFlowTest.Mcp.Contracts;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepFlowTest.Cli;
using ModelContextProtocol.Protocol;

internal enum McpAmbiguityPolicy
{
	Reject,
	First,
}

internal enum McpObserveMode
{
	None,
	Target,
	Tree,
	Delta,
}

internal enum McpObservationFormat
{
	Condensed,
	Json,
}

internal enum McpActionKind
{
	Click,
	Wheel,
	Type,
	Key,
	Set,
	Focus,
	Invoke,
	Drag,
}

internal enum McpMouseButton
{
	Left,
	Right,
	Middle,
}

internal enum McpWaitCondition
{
	Exists,
	Absent,
	ExactCount,
	MinimumCount,
	PropertyEquals,
	PropertyDiffers,
	Enabled,
	Disabled,
	Visible,
	Hidden,
	Stable,
	Responsive,
	WindowTitleChanged,
}

internal enum McpImageFormat
{
	Png,
	Jpeg,
	Bmp,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(McpAttachContextTarget), "attach")]
[JsonDerivedType(typeof(McpLaunchContextTarget), "launch")]
internal abstract record class McpOpenContextTarget;

internal sealed record class McpAttachContextTarget : McpOpenContextTarget
{
	[Description("Existing target process ID. Preferred over process name when attaching.")]
	public int? ProcessId { get; init; }

	[Description("Existing target process name, with or without .exe.")]
	public string? ProcessName { get; init; }

	[Description("Substring of the target's top-level window title.")]
	public string? WindowTitle { get; init; }
}

internal sealed record class McpLaunchContextTarget : McpOpenContextTarget
{
	[Description("Executable path used when mode is launch.")]
	public string FileName { get; init; } = string.Empty;

	[Description("Raw command-line arguments used when launching.")]
	public string? Arguments { get; init; }

	[Description("Working directory used when launching.")]
	public string? WorkingDirectory { get; init; }

	[Description("Terminate a server-launched process when its context is closed.")]
	public bool TerminateOnClose { get; init; }
}

internal sealed record class McpPropertyMatch
{
	[Description("UI property name.")]
	public string Name { get; init; } = string.Empty;

	[Description("Expected property value as text.")]
	public object? Value { get; init; }

	[JsonIgnore]
	public string TextValue => McpValueConversion.ToInvariantString(Value);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(McpHandleSelector), "handle")]
[JsonDerivedType(typeof(McpTargetIdSelector), "target_id")]
[JsonDerivedType(typeof(McpSemanticSelector), "semantic")]
internal abstract record class McpAgentSelector
{
	public virtual string? Handle { get; init; }

	public abstract ElementSelector ToCliSelector();
}

internal sealed record class McpHandleSelector : McpAgentSelector
{
	[Description("Stable element handle returned by deepflow_find.")]
	public override string? Handle { get; init; }

	public override ElementSelector ToCliSelector() => new();
}

internal sealed record class McpTargetIdSelector : McpAgentSelector
{
	[Description("Exact current runtime target ID. Prefer handle or semantic fields across UI revisions.")]
	public string TargetId { get; init; } = string.Empty;

	public override ElementSelector ToCliSelector() => new() { TargetId = TargetId };
}

internal sealed record class McpSemanticSelector : McpAgentSelector
{
	[Description("Exact framework control type, such as Button or TextBox.")]
	public string? Type { get; init; }

	[Description("Substring match against the framework control type.")]
	public string? TypeContains { get; init; }

	[Description("Automation name or framework Name value.")]
	public string? Name { get; init; }

	[Description("AutomationProperties.AutomationId; usually the most stable semantic selector.")]
	public string? AutomationId { get; init; }

	[Description("Readable text or content identity.")]
	public string? Text { get; init; }

	[Description("Require one typed UI property to equal a value.")]
	public McpPropertyMatch? PropertyEquals { get; init; }

	[Description("Require one UI property to contain text.")]
	public McpPropertyMatch? PropertyContains { get; init; }

	[Description("Require one UI property to match a regular expression.")]
	public McpPropertyMatch? PropertyRegex { get; init; }

	[Description("When supplied, require the matched element to have this visibility state.")]
	public bool? Visible { get; init; }

	[Description("When supplied, require the matched element to have this enabled state.")]
	public bool? Enabled { get; init; }

	[Description("Use ordinal case-sensitive text and property matching.")]
	public bool CaseSensitive { get; init; }

	[Description("Explicit zero-based match index. Omit to reject ambiguity.")]
	public int? Index { get; init; }

	[Description("Ambiguous selectors fail by default. Use first only when first-match behavior is intentional.")]
	public McpAmbiguityPolicy AmbiguityPolicy { get; init; } = McpAmbiguityPolicy.Reject;

	[Description("Fallback selector used only when the primary selector has no match; ambiguity never falls through.")]
	public McpSemanticSelector? Fallback { get; init; }

	public override ElementSelector ToCliSelector() =>
		new()
		{
			TypeName = Type,
			TypeContains = TypeContains,
			Name = Name,
			AutomationId = AutomationId,
			Text = Text,
			PropertyEquals = ToPair(PropertyEquals),
			PropertyContains = ToPair(PropertyContains),
			PropertyRegex = ToPair(PropertyRegex),
			Visible = Visible,
			Enabled = Enabled,
			CaseSensitive = CaseSensitive,
			Index = Index,
			First = AmbiguityPolicy == McpAmbiguityPolicy.First,
		};

	private static KeyValuePair<string, string>? ToPair(McpPropertyMatch? match) =>
		match is null ? null : new KeyValuePair<string, string>(match.Name, match.TextValue);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(McpClickAction), "click")]
[JsonDerivedType(typeof(McpMouseWheelAction), "wheel")]
[JsonDerivedType(typeof(McpTypeAction), "type")]
[JsonDerivedType(typeof(McpKeyAction), "key")]
[JsonDerivedType(typeof(McpSetAction), "set")]
[JsonDerivedType(typeof(McpFocusAction), "focus")]
[JsonDerivedType(typeof(McpInvokeAction), "invoke")]
[JsonDerivedType(typeof(McpDragAction), "drag")]
internal abstract record class McpAgentAction;

internal sealed record class McpClickAction : McpAgentAction
{
	[Description("Mouse button to click.")]
	public McpMouseButton Button { get; init; } = McpMouseButton.Left;

	[Description("Number of clicks; use 2 for a double-click.")]
	public int Count { get; init; } = 1;
}

internal sealed record class McpMouseWheelAction : McpAgentAction
{
	[Description("Signed wheel delta; positive scrolls up and negative scrolls down. A standard notch is 120.")]
	public int Delta { get; init; } = 120;
}

internal sealed record class McpTypeAction : McpAgentAction
{
	[Description("Text to type into the resolved element.")]
	public string Text { get; init; } = string.Empty;

	[Description("Select and remove existing text before typing.")]
	public bool ClearFirst { get; init; }
}

internal sealed record class McpKeyAction : McpAgentAction
{
	[Description("Key or chord such as Enter, Tab, or Control+A.")]
	public string Keys { get; init; } = string.Empty;
}

internal sealed record class McpSetAction : McpAgentAction
{
	[Description("Typed property name and value to set.")]
	public McpPropertyMatch Property { get; init; } = new();
}

internal sealed record class McpFocusAction : McpAgentAction;

internal sealed record class McpInvokeAction : McpAgentAction
{
	[Description("Allow-listed operation. Exact values: Focus, Select, Expand, Collapse, Check, Uncheck, BringIntoView, AcceptDialog, or CancelDialog.")]
	public string Operation { get; init; } = string.Empty;
}

internal sealed record class McpDragAction : McpAgentAction
{
	[Description("Element selector for the drop destination.")]
	public McpAgentSelector Destination { get; init; } = new McpSemanticSelector();

	[Description("Drag duration in milliseconds.")]
	public int DurationMs { get; init; } = 500;
}

internal sealed record class McpActionExpectation
{
	[Description("Property and expected value to verify after the action.")]
	public McpPropertyMatch PropertyEquals { get; init; } = new();

	[Description("Maximum time to wait for the expected property value.")]
	public int TimeoutMs { get; init; } = 3_000;
}

internal sealed record class McpContextResult
{
	public string ContextId { get; init; } = string.Empty;

	public int? ProcessId { get; init; }

	public string? ProcessName { get; init; }

	public string? WindowTitle { get; init; }

	public bool IsAlive { get; init; }
}

internal sealed record class McpCloseContextResult
{
	public string ContextId { get; init; } = string.Empty;

	public bool Closed { get; init; }
}

internal sealed record class McpObservationResult
{
	public string ContextId { get; init; } = string.Empty;

	public long Revision { get; init; }

	public int NodeCount { get; init; }

	public string Format { get; init; } = string.Empty;

	[JsonIgnore]
	public string? Text { get; init; }

	public IReadOnlyList<TreeNodeData> Nodes { get; init; } = [];

	[Description("Compact actionable or identifiable elements with stable handles.")]
	public IReadOnlyList<McpElementMatch> Elements { get; init; } = [];

	public string ResourceUri { get; init; } = string.Empty;
}

internal sealed record class McpFindResult
{
	public string ContextId { get; init; } = string.Empty;

	public long Revision { get; init; }

	public int MatchCount { get; init; }

	public IReadOnlyList<McpElementMatch> Matches { get; init; } = [];
}

internal sealed record class McpElementMatch
{
	public string Handle { get; init; } = string.Empty;

	public string TargetId { get; init; } = string.Empty;

	public string? Type { get; init; }

	public string? AutomationId { get; init; }

	public string? Name { get; init; }

	public string? Text { get; init; }

	public string? Path { get; init; }

	public IReadOnlyDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();
}

internal sealed record class McpSelectorSuggestionsResult
{
	public string TargetId { get; init; } = string.Empty;

	public IReadOnlyList<McpSelectorSuggestion> Suggestions { get; init; } = [];
}

internal sealed record class McpSelectorSuggestion
{
	public McpAgentSelector Selector { get; init; } = new McpSemanticSelector();

	public double Confidence { get; init; }

	public string Stability { get; init; } = string.Empty;

	public bool Unique { get; init; }

	public string Explanation { get; init; } = string.Empty;
}

internal sealed record class McpResolvedElement
{
	public string? Handle { get; init; }

	public string TargetId { get; init; } = string.Empty;

	public string Strategy { get; init; } = string.Empty;

	public double Confidence { get; init; }

	public long OriginalRevision { get; init; }

	public long CurrentRevision { get; init; }
}

internal sealed record class McpVerificationResult
{
	public bool Passed { get; init; }

	public long ElapsedMs { get; init; }

	public string? Message { get; init; }
}

internal sealed record class McpActionResult
{
	public string ContextId { get; init; } = string.Empty;

	public McpActionKind Action { get; init; }

	public long RevisionBefore { get; init; }

	public long RevisionAfter { get; init; }

	public McpResolvedElement? Resolved { get; init; }

	public McpVerificationResult? Verification { get; init; }

	[JsonIgnore]
	public string? Observation { get; init; }

	public McpActionDelta? Delta { get; init; }

	public IReadOnlyList<McpElementMatch> Elements { get; init; } = [];
}

internal sealed record class McpActionDelta
{
	public bool HasChanges { get; init; }

	public IReadOnlyList<McpElementMatch> Added { get; init; } = [];

	public IReadOnlyList<McpElementMatch> Changed { get; init; } = [];

	public IReadOnlyList<McpRemovedElement> Removed { get; init; } = [];
}

internal sealed record class McpRemovedElement
{
	public string? Handle { get; init; }

	public string TargetId { get; init; } = string.Empty;
}

internal sealed record class McpWaitResult
{
	public string ContextId { get; init; } = string.Empty;

	public McpWaitCondition Condition { get; init; }

	public bool Satisfied { get; init; }

	public long ElapsedMs { get; init; }

	public long Revision { get; init; }

	public int MatchCount { get; init; }

	public IReadOnlyList<McpElementMatch> Matches { get; init; } = [];

	public string? WindowTitle { get; init; }
}

internal sealed record class McpCaptureResult
{
	public string ContextId { get; init; } = string.Empty;

	public string MimeType { get; init; } = string.Empty;

	public int Width { get; init; }

	public int Height { get; init; }

	public long Revision { get; init; }

	public string? TargetId { get; init; }

	public string ResourceUri { get; init; } = string.Empty;
}

internal sealed record class McpDiagnosisResult
{
	public string ContextId { get; init; } = string.Empty;

	public bool IsAlive { get; init; }

	public bool IsResponsive { get; init; }

	public string Summary { get; init; } = string.Empty;

	public int BindingFailureCount { get; init; }

	public int RecentLogCount { get; init; }

	public IReadOnlyList<McpDiagnosticLogEntry> RecentLogs { get; init; } = [];

	public string? TargetErrorCode { get; init; }

	public string? DiagnosticErrorCode { get; init; }

	public string? SuggestedRecovery { get; init; }

	public long Revision { get; init; }

	public string ResourceUri { get; init; } = string.Empty;
}

internal sealed record class McpDiagnosticLogEntry
{
	public string? ContextId { get; init; }

	public long Sequence { get; init; }

	public DateTimeOffset TimestampUtc { get; init; }

	public string Level { get; init; } = string.Empty;

	public string Code { get; init; } = string.Empty;

	public string Message { get; init; } = string.Empty;
}

internal sealed record class McpAgentToolError
{
	public string Code { get; init; } = string.Empty;

	public string Message { get; init; } = string.Empty;

	public bool Retryable { get; init; }

	public bool SafeToRepeat { get; init; }

	public object? Details { get; init; }

	public McpRecoveryDirective? Recovery { get; init; }

	public string? ContextId { get; init; }

	public long? Revision { get; init; }
}

internal sealed record class McpRecoveryDirective
{
	public string Kind { get; init; } = string.Empty;

	public string? Message { get; init; }

	public string? SuggestedNextOperation { get; init; }

	public McpAgentSelector? Selector { get; init; }
}

internal sealed record class McpStaleElementDetails
{
	public string Handle { get; init; } = string.Empty;

	public long OriginalRevision { get; init; }

	public long CurrentRevision { get; init; }

	public McpAgentSelector Selector { get; init; } = new McpSemanticSelector();
}

internal sealed record class McpAmbiguousElementDetails
{
	public int MatchCount { get; init; }

	public IReadOnlyList<McpAmbiguousElementCandidate> Candidates { get; init; } = [];
}

internal sealed record class McpAmbiguousElementCandidate
{
	public string Handle { get; init; } = string.Empty;

	public string TargetId { get; init; } = string.Empty;

	public string? Type { get; init; }

	public string? AutomationId { get; init; }

	public string? Name { get; init; }

	public string? Text { get; init; }

	public string? Path { get; init; }
}

internal static class McpCallToolResults
{
	private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

	public static CallToolResult FromLegacy<T>(
		McpToolResponse response,
		Func<object?, T> convert,
		Func<T, IList<ContentBlock>>? content = null,
		string? contextId = null,
		long? revision = null)
	{
		if (!response.Success)
			return Error(response, contextId, revision);

		var result = convert(response.Data);
		var structured = JsonSerializer.SerializeToElement(result, JsonOptions);
		return new CallToolResult
		{
			IsError = false,
			StructuredContent = structured,
			Content = content?.Invoke(result) ?? [new TextContentBlock { Text = "Result returned as structured content." }],
		};
	}

	public static CallToolResult Error(McpToolResponse response, string? contextId = null, long? revision = null)
	{
		var source = response.Error ?? new McpToolError { Code = CliErrorCodes.UnexpectedError, Message = "Tool execution failed." };
		var error = new McpAgentToolError
		{
			Code = NormalizeCode(source.Code, source.Details),
			Message = source.Message,
			Retryable = IsRetryable(source.Code),
			SafeToRepeat = IsSafeToRepeat(source.Code),
			Details = source.Details,
			Recovery = CreateRecovery(source.Code, source.Details, response.Recovery),
			ContextId = contextId ?? response.Target?.ContextId,
			Revision = revision ?? response.Target?.Revision,
		};
		return new CallToolResult
		{
			IsError = true,
			StructuredContent = JsonSerializer.SerializeToElement(error, JsonOptions),
			Content = [new TextContentBlock { Text = $"{error.Code}: {error.Message}" }],
		};
	}

	private static string NormalizeCode(string code, object? details) =>
		code switch
		{
			CliErrorCodes.AmbiguousTarget => "ambiguous_element",
			CliErrorCodes.StaleTarget when details is McpStaleElementDetails => "stale_element",
			CliErrorCodes.StaleTarget => "stale_context",
			_ => code.Replace('-', '_'),
		};

	private static bool IsRetryable(string code) =>
		code is CliErrorCodes.InvalidArguments
			or CliErrorCodes.TargetNotFound
			or CliErrorCodes.NoMatch
			or CliErrorCodes.AmbiguousTarget
			or CliErrorCodes.StaleTarget
			or CliErrorCodes.CommandTimeout
			or CliErrorCodes.TargetExited
			or CliErrorCodes.PipeFailed
			or CliErrorCodes.ProtocolError;

	private static bool IsSafeToRepeat(string code) =>
		code is CliErrorCodes.InvalidArguments
			or CliErrorCodes.TargetNotFound
			or CliErrorCodes.NoMatch
			or CliErrorCodes.AmbiguousTarget
			or CliErrorCodes.StaleTarget;

	private static McpRecoveryDirective? CreateRecovery(string code, object? details, string? message)
	{
		var kind = code switch
		{
			CliErrorCodes.AmbiguousTarget => "refine_selector",
			CliErrorCodes.StaleTarget when details is McpStaleElementDetails => "refresh_and_resolve",
			CliErrorCodes.StaleTarget => "open_context",
			CliErrorCodes.NoMatch or CliErrorCodes.TargetNotFound => "observe_and_find",
			CliErrorCodes.CommandTimeout => "increase_timeout_or_diagnose",
			CliErrorCodes.TargetExited => "open_context",
			CliErrorCodes.PipeFailed or CliErrorCodes.ProtocolError => "diagnose_context",
			CliErrorCodes.InvalidArguments => "correct_arguments",
			_ when message is not null => "follow_guidance",
			_ => string.Empty,
		};
		if (kind.Length == 0)
			return null;

		return new McpRecoveryDirective
		{
			Kind = kind,
			Message = message,
			Selector = (details as McpStaleElementDetails)?.Selector,
			SuggestedNextOperation = kind switch
			{
				"refine_selector" or "observe_and_find" or "refresh_and_resolve" => "deepflow_find",
				"diagnose_context" or "increase_timeout_or_diagnose" => "deepflow_diagnose",
				"open_context" => "deepflow_open_context",
				_ => null,
			},
		};
	}

	private static JsonSerializerOptions CreateJsonOptions()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
		return options;
	}
}

internal static class McpValueConversion
{
	public static string ToInvariantString(object? value)
	{
		if (value is JsonElement element)
		{
			return element.ValueKind switch
			{
				JsonValueKind.String => element.GetString() ?? string.Empty,
				JsonValueKind.True => bool.TrueString,
				JsonValueKind.False => bool.FalseString,
				JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
				_ => element.GetRawText(),
			};
		}

		return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
	}

	public static object? ToProtocolValue(object? value)
	{
		if (value is not JsonElement element)
			return value;

		return element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
			JsonValueKind.Number => element.GetDouble(),
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			_ => element.Clone(),
		};
	}
}
