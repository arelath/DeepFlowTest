namespace DeepFlowTest.Mcp.Tools;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

[McpServerToolType]
internal static class ActionTools
{
	private static readonly HashSet<string> KnownRoutedEvents = new(StringComparer.Ordinal)
	{
		"Click",
		"MouseDoubleClick",
		"Checked",
		"Unchecked",
		"Expanded",
		"Collapsed",
	};

	private static readonly HashSet<string> KnownOperations = new(StringComparer.Ordinal)
	{
		"Focus",
		"AcceptDialog",
		"CancelDialog",
		"BringIntoView",
		"Select",
		"Expand",
		"Collapse",
		"Check",
		"Uncheck",
	};

	[McpServerTool(Name = "deepflow_click_element"), Description("Click an element resolved by target ID or selector.")]
	public static McpToolResponse ClickElement(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string? targetId = null,
		string? typeName = null,
		string? name = null,
		string? automationId = null,
		string? text = null,
		string? property = null,
		string? button = "left",
		int clickCount = 1,
		bool doubleClick = false,
		string? after = "delta")
	{
		return ExecuteAction(
			runner,
			host,
			cache,
			options,
			"click",
			after,
			selector: CreateSelector(targetId, typeName, null, name, automationId, text, property),
			parameters: new { targetId, typeName, name, automationId, text, property, button, clickCount, doubleClick, after },
			action =>
			{
				if (clickCount <= 0)
					throw new CliException(CliErrorCodes.InvalidArguments, "clickCount must be greater than zero.");

				var mouseButton = McpArgumentParsing.ParseMouseButton(button, MouseButtonKind.Left);
				return action.Execute(
					targetId => doubleClick
						? new KnownRoutedEventCommandRequest { TargetId = targetId ?? string.Empty, EventName = "MouseDoubleClick" }
						: new ClickCommandRequest
						{
							TargetId = targetId ?? string.Empty,
							MouseButton = mouseButton,
							ClickCount = clickCount,
						},
					requireElementTarget: true);
			});
	}

	[McpServerTool(Name = "deepflow_drag_and_drop"), Description("Drag a source element and drop it on a destination element. Mutates UI and requires allowActions.")]
	public static McpToolResponse DragAndDrop(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string? targetId = null,
		string? typeName = null,
		string? name = null,
		string? automationId = null,
		string? text = null,
		string? property = null,
		string? destinationTargetId = null,
		string? destinationTypeName = null,
		string? destinationName = null,
		string? destinationAutomationId = null,
		string? destinationText = null,
		string? destinationProperty = null,
		int durationMs = 500,
		int holdMs = 75,
		int stepIntervalMs = 16,
		int postDropWaitMs = 100,
		double sourceAnchorX = 0.5,
		double sourceAnchorY = 0.5,
		double destinationAnchorX = 0.5,
		double destinationAnchorY = 0.5,
		bool ensureForeground = false,
		bool validateSameProcess = true,
		string? after = "delta")
	{
		return runner.Run(() =>
		{
			if (!options.Value.Policy.AllowActions)
				throw new CliException(CliErrorCodes.ActionDenied, "Action 'drag' requires allowActions policy.");

			var session = host.RequireSession();
			var afterMode = NormalizeAfterMode(after);
			var beforeSnapshot = CaptureDeltaBaseline(host, cache, options.Value, afterMode);
			var common = CreateCommonOptions(options.Value, afterMode);
			var defaults = CreateDefaults(options.Value);
			var sourceSelector = CreateSelector(targetId, typeName, null, name, automationId, text, property).ToCliSelector();
			var destinationSelector = CreateSelector(destinationTargetId, destinationTypeName, null, destinationName, destinationAutomationId, destinationText, destinationProperty).ToCliSelector();
			var result = new ActionCommandSupport().ExecuteTwoTarget(
				"drag",
				session.AppSession,
				common,
				defaults,
				sourceSelector,
				destinationSelector,
				(sourceTargetId, resolvedDestinationTargetId) => new DragAndDropCommandRequest
				{
					TargetId = sourceTargetId,
					DestinationTargetId = resolvedDestinationTargetId,
					DurationMs = durationMs,
					HoldMs = holdMs,
					StepIntervalMs = stepIntervalMs,
					PostDropWaitMs = postDropWaitMs,
					SourceAnchorX = sourceAnchorX,
					SourceAnchorY = sourceAnchorY,
					DestinationAnchorX = destinationAnchorX,
					DestinationAnchorY = destinationAnchorY,
					UseInjectedEvents = true,
					EnsureForeground = ensureForeground,
					ValidateSameProcess = validateSameProcess,
					TimeoutMs = common.TimeoutMs,
				});
			cache.Invalidate();
			return CreateMcpActionResult(
				result.Action,
				source: result.Source,
				destination: result.Destination,
				target: null,
				payload: result.Payload,
				after: result.After,
				delta: CreateDeltaAfter(host, cache, options.Value, afterMode, beforeSnapshot));
		}, new
		{
			targetId,
			typeName,
			name,
			automationId,
			text,
			property,
			destinationTargetId,
			destinationTypeName,
			destinationName,
			destinationAutomationId,
			destinationText,
			destinationProperty,
			durationMs,
			holdMs,
			stepIntervalMs,
			postDropWaitMs,
			sourceAnchorX,
			sourceAnchorY,
			destinationAnchorX,
			destinationAnchorY,
			ensureForeground,
			validateSameProcess,
			after,
		});
	}

	[McpServerTool(Name = "deepflow_focus_element"), Description("Move focus to an element resolved by target ID or selector.")]
	public static McpToolResponse FocusElement(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string? targetId = null,
		string? typeName = null,
		string? name = null,
		string? automationId = null,
		string? text = null,
		string? property = null,
		string? after = "delta")
	{
		return ExecuteAction(
			runner,
			host,
			cache,
			options,
			"focus",
			after,
			CreateSelector(targetId, typeName, null, name, automationId, text, property),
			new { targetId, typeName, name, automationId, text, property, after },
			action => action.Execute(
				targetId => new FocusCommandRequest { TargetId = targetId ?? string.Empty },
				requireElementTarget: true,
				afterProperties: [KnownProperties.IsFocused, KnownProperties.IsKeyboardFocused, KnownProperties.IsKeyboardFocusWithin]));
	}

	[McpServerTool(Name = "deepflow_type_text"), Description("Type text into an element or the current focused target.")]
	public static McpToolResponse TypeText(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string text,
		string? targetId = null,
		string? typeName = null,
		string? name = null,
		string? automationId = null,
		string? selectorText = null,
		string? property = null,
		bool clearFirst = false,
		string? after = "delta")
	{
		return ExecuteAction(
			runner,
			host,
			cache,
			options,
			"type",
			after,
			CreateSelector(targetId, typeName, null, name, automationId, selectorText, property),
			new { text, targetId, typeName, name, automationId, selectorText, property, clearFirst, after },
			action => action.Execute(
				targetId => new TypeTextCommandRequest
				{
					TargetId = targetId,
					Text = text,
					ClearFirst = clearFirst,
				},
				requireElementTarget: false,
				afterProperties: [KnownProperties.Text, KnownProperties.Content]));
	}

	[McpServerTool(Name = "deepflow_press_keys"), Description("Send a key chord such as Enter, Ctrl+A, or Tab.")]
	public static McpToolResponse PressKeys(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string keys,
		string? targetId = null,
		string? typeName = null,
		string? name = null,
		string? automationId = null,
		string? text = null,
		string? property = null,
		int delayMs = TimeoutDefaults.KeyboardDelayMs,
		bool ensureForeground = true,
		string? after = "delta")
	{
		return ExecuteAction(
			runner,
			host,
			cache,
			options,
			"key",
			after,
			CreateSelector(targetId, typeName, null, name, automationId, text, property),
			new { keys, targetId, typeName, name, automationId, text, property, delayMs, ensureForeground, after },
			action => action.Execute(
				targetId => new KeyPressCommandRequest
				{
					TargetId = targetId,
					Keys = keys,
					DelayMs = delayMs,
					EnsureForeground = ensureForeground,
				},
				requireElementTarget: false,
				afterProperties: [KnownProperties.Text, KnownProperties.Content, KnownProperties.IsKeyboardFocused, KnownProperties.IsKeyboardFocusWithin]));
	}

	[McpServerTool(Name = "deepflow_set_property"), Description("Set a supported property on an element.")]
	public static McpToolResponse SetProperty(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string propertyName,
		string value,
		string? targetId = null,
		string? typeName = null,
		string? name = null,
		string? automationId = null,
		string? text = null,
		string? property = null,
		string? after = "delta")
	{
		return ExecuteAction(
			runner,
			host,
			cache,
			options,
			"set",
			after,
			CreateSelector(targetId, typeName, null, name, automationId, text, property),
			new { propertyName, value, targetId, typeName, name, automationId, text, property, after },
			action => action.Execute(
				targetId => new SetPropertyCommandRequest
				{
					TargetId = targetId ?? string.Empty,
					PropertyName = propertyName,
					PropertyValue = McpArgumentParsing.ParseJsonScalar(value),
				},
				requireElementTarget: true,
				afterProperties: [propertyName]));
	}

	[McpServerTool(Name = "deepflow_raise_event"), Description("Raise an allow-listed routed event on an element.")]
	public static McpToolResponse RaiseEvent(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string eventName,
		string? targetId = null,
		string? typeName = null,
		string? name = null,
		string? automationId = null,
		string? text = null,
		string? property = null,
		string? after = "delta")
	{
		return ExecuteAction(runner, host, cache, options, "raise", after, CreateSelector(targetId, typeName, null, name, automationId, text, property), new { eventName, targetId, typeName, name, automationId, text, property, after }, action =>
		{
			if (!KnownRoutedEvents.Contains(eventName))
				throw new CliException(CliErrorCodes.InvalidArguments, $"Routed event '{eventName}' is not allow-listed.");

			return action.Execute(
				targetId => new KnownRoutedEventCommandRequest
				{
					TargetId = targetId ?? string.Empty,
					EventName = eventName,
				},
				requireElementTarget: true);
		});
	}

	[McpServerTool(Name = "deepflow_invoke_operation"), Description("Invoke an allow-listed known operation on an element.")]
	public static McpToolResponse InvokeOperation(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string operation,
		string? targetId = null,
		string? typeName = null,
		string? name = null,
		string? automationId = null,
		string? text = null,
		string? property = null,
		string? after = "delta")
	{
		return ExecuteAction(runner, host, cache, options, "invoke", after, CreateSelector(targetId, typeName, null, name, automationId, text, property), new { operation, targetId, typeName, name, automationId, text, property, after }, action =>
		{
			if (!KnownOperations.Contains(operation))
				throw new CliException(CliErrorCodes.InvalidArguments, $"Known operation '{operation}' is not allow-listed.");

			return action.Execute(
				targetId => new KnownOperationCommandRequest
				{
					TargetId = targetId ?? string.Empty,
					Operation = operation,
				},
				requireElementTarget: true);
		});
	}

	private static McpToolResponse ExecuteAction(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string actionName,
		string? after,
		McpElementSelector selector,
		object parameters,
		Func<ActionInvocation, ActionCommandResult> execute)
	{
		return runner.Run(() =>
		{
			if (!options.Value.Policy.AllowActions)
				throw new CliException(CliErrorCodes.ActionDenied, $"Action '{actionName}' requires allowActions policy.");

			var session = host.RequireSession();
			var afterMode = NormalizeAfterMode(after);
			var beforeSnapshot = CaptureDeltaBaseline(host, cache, options.Value, afterMode);
			var common = CreateCommonOptions(options.Value, afterMode);
			var defaults = CreateDefaults(options.Value);
			var invocation = new ActionInvocation(actionName, session.AppSession, common, defaults, selector.ToCliSelector());
			var result = execute(invocation);
			cache.Invalidate();
			return CreateMcpActionResult(
				result.Action,
				source: null,
				destination: null,
				target: result.Target,
				payload: result.Payload,
				after: result.After,
				delta: CreateDeltaAfter(host, cache, options.Value, afterMode, beforeSnapshot));
		}, parameters);
	}

	private static string NormalizeAfterMode(string? after)
	{
		var afterMode = string.IsNullOrWhiteSpace(after) ? "none" : after.Trim().ToLowerInvariant();
		if (afterMode is not ("none" or "target" or "tree" or "delta"))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Invalid after mode '{after}'.");

		return afterMode;
	}

	private static CliCommonOptions CreateCommonOptions(DeepFlowMcpOptions options, string? afterMode)
	{
		var cliAfterMode = string.Equals(afterMode, "delta", StringComparison.Ordinal) ? "none" : afterMode ?? "none";
		return new CliCommonOptions
		{
			TimeoutMs = options.DefaultTimeoutMs,
			UseShortIds = true,
			AllowActions = true,
			AllowArbitraryInvoke = options.Policy.AllowArbitraryInvoke,
			After = cliAfterMode,
		};
	}

	private static CliDefaults CreateDefaults(DeepFlowMcpOptions options) =>
		new()
		{
			TreeLimit = options.TreeLimit,
			PropertyNames = [.. options.DefaultProperties],
		};

	private static McpElementSelector CreateSelector(
		string? targetId,
		string? typeName,
		string? typeContains,
		string? name,
		string? automationId,
		string? text,
		string? property) =>
		new()
		{
			TargetId = targetId,
			TypeName = typeName,
			TypeContains = typeContains,
			Name = name,
			AutomationId = automationId,
			Text = text,
			PropertyEquals = McpArgumentParsing.ParsePair(property, nameof(property)),
			Visible = true,
			First = true,
		};

	private static VisualTreeSnapshot? CaptureDeltaBaseline(
		McpSessionHost host,
		McpSnapshotCache cache,
		DeepFlowMcpOptions options,
		string afterMode) =>
		string.Equals(afterMode, "delta", StringComparison.Ordinal)
			? cache.GetOrRefresh(
				host,
				McpSemanticRecordingFormatter.MergeSemanticProperties(options.DefaultProperties),
				options.TreeLimit,
				includeHidden: true,
				refresh: true)
			: null;

	private static McpCondensedRecordingOutput? CreateDeltaAfter(
		McpSessionHost host,
		McpSnapshotCache cache,
		DeepFlowMcpOptions options,
		string afterMode,
		VisualTreeSnapshot? beforeSnapshot)
	{
		if (!string.Equals(afterMode, "delta", StringComparison.Ordinal) || beforeSnapshot is null)
			return null;

		var afterSnapshot = cache.GetOrRefresh(
			host,
			McpSemanticRecordingFormatter.MergeSemanticProperties(options.DefaultProperties),
			options.TreeLimit,
			includeHidden: true,
			refresh: true);
		return McpSemanticRecordingFormatter.FormatDelta(beforeSnapshot, afterSnapshot);
	}

	private static McpActionCommandResult CreateMcpActionResult(
		string action,
		TreeNodeData? source,
		TreeNodeData? destination,
		TreeNodeData? target,
		object? payload,
		object? after,
		McpCondensedRecordingOutput? delta)
	{
		return new McpActionCommandResult
		{
			Action = action,
			Source = source,
			Destination = destination,
			Target = target,
			Payload = payload,
			After = delta ?? after,
		};
	}

	private sealed class ActionInvocation
	{
		private readonly string actionName;
		private readonly ICliAppSession session;
		private readonly CliCommonOptions commonOptions;
		private readonly CliDefaults defaults;
		private readonly ElementSelector selector;

		public ActionInvocation(
			string actionName,
			ICliAppSession session,
			CliCommonOptions commonOptions,
			CliDefaults defaults,
			ElementSelector selector)
		{
			this.actionName = actionName;
			this.session = session;
			this.commonOptions = commonOptions;
			this.defaults = defaults;
			this.selector = selector;
		}

		public ActionCommandResult Execute(
			Func<string?, IpcCommand> createCommand,
			bool requireElementTarget,
			IReadOnlyList<string>? afterProperties = null) =>
			new ActionCommandSupport().Execute(
				actionName,
				session,
				commonOptions,
				defaults,
				selector,
				createCommand,
				requireElementTarget,
				afterProperties);
	}
}
