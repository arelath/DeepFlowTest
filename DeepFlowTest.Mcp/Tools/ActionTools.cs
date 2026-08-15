namespace DeepFlowTest.Mcp.Tools;

using System;
using System.ComponentModel;
using DeepFlowTest.Automation;
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
			after,
			selector: CreateSelector(targetId, typeName, null, name, automationId, text, property),
			parameters: new { targetId, typeName, name, automationId, text, property, button, clickCount, doubleClick, after },
			createAction: () => new ClickAction(McpArgumentParsing.ParseMouseButton(button, MouseButtonKind.Left), clickCount, doubleClick));
	}

	[McpServerTool(Name = "deepflow_mouse_wheel"), Description("Send mouse-wheel input to an element resolved by target ID or selector.")]
	public static McpToolResponse MouseWheel(
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
		int delta = 120,
		string? after = "delta")
	{
		return ExecuteAction(
			runner,
			host,
			cache,
			options,
			after,
			selector: CreateSelector(targetId, typeName, null, name, automationId, text, property),
			parameters: new { targetId, typeName, name, automationId, text, property, delta, after },
			createAction: () => new MouseWheelAction(delta));
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
		return ExecuteAction(
			runner,
			host,
			cache,
			options,
			after,
			CreateSelector(targetId, typeName, null, name, automationId, text, property),
			new
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
		},
			() => new DragAction(durationMs, holdMs, stepIntervalMs, postDropWaitMs, sourceAnchorX, sourceAnchorY,
				destinationAnchorX, destinationAnchorY, UseInjectedEvents: true,
				EnsureForeground: ensureForeground, ValidateSameProcess: validateSameProcess),
			CreateSelector(destinationTargetId, destinationTypeName, null, destinationName, destinationAutomationId, destinationText, destinationProperty));
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
			after,
			CreateSelector(targetId, typeName, null, name, automationId, text, property),
			new { targetId, typeName, name, automationId, text, property, after },
			createAction: () => new FocusAction());
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
			after,
			CreateSelector(targetId, typeName, null, name, automationId, selectorText, property),
			new { text, targetId, typeName, name, automationId, selectorText, property, clearFirst, after },
			createAction: () => new TypeTextAction(text, clearFirst));
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
			after,
			CreateSelector(targetId, typeName, null, name, automationId, text, property),
			new { keys, targetId, typeName, name, automationId, text, property, delayMs, ensureForeground, after },
			createAction: () => new KeyPressAction(keys, delayMs, ensureForeground));
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
			after,
			CreateSelector(targetId, typeName, null, name, automationId, text, property),
			new { propertyName, value, targetId, typeName, name, automationId, text, property, after },
			createAction: () => new SetPropertyAction(propertyName, McpArgumentParsing.ParseJsonScalar(value)));
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
		return ExecuteAction(runner, host, cache, options, after,
			CreateSelector(targetId, typeName, null, name, automationId, text, property),
			new { eventName, targetId, typeName, name, automationId, text, property, after },
			() => new RoutedEventAction(eventName));
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
		return ExecuteAction(runner, host, cache, options, after,
			CreateSelector(targetId, typeName, null, name, automationId, text, property),
			new { operation, targetId, typeName, name, automationId, text, property, after },
			() => new KnownOperationAction(operation));
	}

	private static McpToolResponse ExecuteAction(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string? after,
		McpElementSelector selector,
		object parameters,
		Func<AutomationAction> createAction,
		McpElementSelector? destination = null)
	{
		return runner.Run(() =>
		{
			var action = createAction();
			var pipeline = new AutomationActionPipeline();
			var descriptor = pipeline.Prepare(
				action,
				new AutomationActionPipelineHooks
				{
					DemandPolicy = actionDescriptor =>
					{
						if (!options.Value.Policy.AllowActions)
							throw new AutomationException(AutomationErrorCodes.ActionDenied, $"Action '{actionDescriptor.Name}' requires allowActions policy.");
					},
				});
			var session = host.RequireSession();
			var afterMode = NormalizeAfterMode(after);
			var beforeSnapshot = CaptureDeltaBaseline(host, cache, options.Value, afterMode);
			var executionOptions = CreateExecutionOptions(options.Value, afterMode);
			var result = pipeline.ExecutePrepared(
				session.AppSession,
				executionOptions,
				new AutomationActionRequest(action, selector.ToAutomationSelector(), destination?.ToAutomationSelector()),
				descriptor,
				new AutomationActionPipelineHooks
				{
					InvalidateCache = cache.Invalidate,
				});
			var single = result.SingleTarget;
			var two = result.TwoTarget;
			return CreateMcpActionResult(
				result.Action,
				source: two?.Source,
				destination: two?.Destination,
				target: single?.Target,
				payload: single?.Payload ?? two?.Payload,
				after: single?.After ?? two?.After,
				delta: CreateDeltaAfter(host, cache, options.Value, afterMode, beforeSnapshot));
		}, parameters);
	}

	private static string NormalizeAfterMode(string? after)
	{
		var afterMode = string.IsNullOrWhiteSpace(after) ? "none" : after.Trim().ToLowerInvariant();
		if (afterMode is not ("none" or "target" or "tree" or "delta"))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Invalid after mode '{after}'.");

		return afterMode;
	}

	private static AutomationExecutionOptions CreateExecutionOptions(DeepFlowMcpOptions options, string? afterMode) =>
		new(
			options.DefaultTimeoutMs,
			options.TreeLimit,
			[.. options.DefaultProperties],
			afterMode switch
			{
				"tree" => ObservationMode.Tree,
				"target" => ObservationMode.Target,
				_ => ObservationMode.None,
			},
			UseShortIds: true);

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
			First = false,
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

	private static McpActionExecutionResult CreateMcpActionResult(
		string action,
		TreeNodeData? source,
		TreeNodeData? destination,
		TreeNodeData? target,
		object? payload,
		object? after,
		McpCondensedRecordingOutput? delta)
	{
		return new McpActionExecutionResult
		{
			Action = action,
			Source = source,
			Destination = destination,
			Target = target,
			Payload = payload,
			After = delta ?? after,
		};
	}

}
