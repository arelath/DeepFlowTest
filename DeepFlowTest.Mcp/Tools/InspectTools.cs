namespace DeepFlowTest.Mcp.Tools;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.Resources;
using DeepFlowTest.Interop;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

[McpServerToolType]
internal static class InspectTools
{
	[McpServerTool(Name = "deepflow_get_visual_tree"), Description("Read and shape the attached target's visual tree.")]
	public static McpToolResponse GetVisualTree(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		DeepFlowResourceStore resources,
		IOptions<DeepFlowMcpOptions> options,
		[Description("Tree shape: flat or nested.")] string? shape = "flat",
		[Description("Comma-separated property names, 'default', or 'none'.")] string? properties = null,
		[Description("Maximum output nodes.")] int? limit = null,
		[Description("Maximum traversal depth, or -1 for unlimited.")] int maxDepth = -1,
		[Description("Include hidden nodes.")] bool includeHidden = false,
		[Description("Optional root target ID or short ID.")] string? rootTargetId = null,
		[Description("Force a fresh target snapshot.")] bool refresh = false,
		[Description("Output format: condensed-agent or json.")] string? outputFormat = McpSemanticRecordingFormatter.FormatName)
	{
		return runner.Run(() =>
		{
			var useCondensed = IsCondensedOutput(outputFormat);
			var propertyNames = McpArgumentParsing.ParseProperties(properties, options.Value.DefaultProperties);
			if (useCondensed)
				propertyNames = McpSemanticRecordingFormatter.MergeSemanticProperties(propertyNames);

			var snapshot = cache.GetOrRefresh(host, EnsureVisibilityProperty(propertyNames, includeHidden), Math.Max(options.Value.TreeLimit, limit ?? options.Value.TreeLimit), includeHidden: true, refresh, rootTargetId);
			if (useCondensed)
			{
				var recording = McpSemanticRecordingFormatter.FormatSnapshot(snapshot);
				resources.StoreText(DeepFlowResourceNames.LatestVisualTree, recording.Text, "text/plain");
				return recording;
			}

			var tree = new TreeSnapshotService().Shape(snapshot, new TreeSnapshotOptions
			{
				Shape = McpArgumentParsing.ParseTreeShape(shape, TreeShape.Flat),
				RootTargetId = rootTargetId,
				MaxDepth = maxDepth,
				Limit = limit ?? options.Value.TreeLimit,
				IncludeHidden = includeHidden,
				IncludeTypeNames = true,
				IncludePath = true,
				Properties = propertyNames,
				UseShortIds = true,
			});
			resources.StoreJson(DeepFlowResourceNames.LatestVisualTree, tree);
			return tree;
		}, new { shape, properties, limit, maxDepth, includeHidden, rootTargetId, refresh, outputFormat });
	}

	[McpServerTool(Name = "deepflow_find_elements"), Description("Find elements in the attached target's visual tree.")]
	public static McpToolResponse FindElements(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string? typeName = null,
		string? typeContains = null,
		string? name = null,
		string? automationId = null,
		string? text = null,
		[Description("Property equality filter as name=value.")] string? property = null,
		[Description("Property contains filter as name=value.")] string? contains = null,
		[Description("Property regex filter as name=value.")] string? regex = null,
		bool? visible = null,
		bool? enabled = null,
		bool caseSensitive = false,
		int limit = 50,
		bool includeProperties = true,
		bool includeAncestors = false,
		bool includeChildren = false,
		[Description("Comma-separated property names, 'default', or 'none'.")] string? properties = null,
		bool refresh = false)
	{
		return runner.Run(() =>
		{
			var propertyNames = McpArgumentParsing.ParseProperties(properties, options.Value.DefaultProperties);
			var snapshot = cache.GetOrRefresh(host, propertyNames, Math.Max(options.Value.TreeLimit, limit), refresh: refresh);
			return new FindSnapshotService().Find(snapshot, new FindSnapshotOptions
			{
				TypeName = typeName,
				TypeContains = typeContains,
				Name = name,
				AutomationId = automationId,
				Text = text,
				PropertyEquals = McpArgumentParsing.ParsePair(property, nameof(property)),
				PropertyContains = McpArgumentParsing.ParsePair(contains, nameof(contains)),
				PropertyRegex = McpArgumentParsing.ParsePair(regex, nameof(regex)),
				Visible = visible,
				Enabled = enabled,
				CaseSensitive = caseSensitive,
				Limit = limit,
				IncludePath = true,
				IncludeProperties = includeProperties,
				IncludeAncestors = includeAncestors,
				IncludeChildren = includeChildren,
				Properties = propertyNames,
				UseShortIds = true,
			});
		}, new
		{
			typeName,
			typeContains,
			name,
			automationId,
			text,
			property,
			contains,
			regex,
			visible,
			enabled,
			caseSensitive,
			limit,
			includeProperties,
			includeAncestors,
			includeChildren,
			properties,
			refresh,
		});
	}

	[McpServerTool(Name = "deepflow_get_node"), Description("Get one visual tree node by full target ID or short ID.")]
	public static McpToolResponse GetNode(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		DeepFlowResourceStore resources,
		IOptions<DeepFlowMcpOptions> options,
		string targetId,
		bool includeAncestors = false,
		bool includeChildren = false,
		bool includeSubtree = false,
		int subtreeDepth = -1,
		[Description("Comma-separated property names, 'default', or 'none'.")] string? properties = null,
		bool refresh = false)
	{
		return runner.Run(() =>
		{
			var propertyNames = McpArgumentParsing.ParseProperties(properties, options.Value.DefaultProperties);
			var snapshot = cache.GetOrRefresh(host, propertyNames, options.Value.TreeLimit, refresh: refresh);
			var node = new NodeSnapshotService().GetNode(snapshot, new NodeSnapshotOptions
			{
				TargetId = targetId,
				IncludeAncestors = includeAncestors,
				IncludeChildren = includeChildren,
				IncludeSubtree = includeSubtree,
				SubtreeDepth = subtreeDepth,
				IncludePath = true,
				UseShortIds = true,
				Properties = propertyNames,
			});
			resources.StoreJson(DeepFlowResourceNames.LatestNode, node);
			return node;
		}, new { targetId, includeAncestors, includeChildren, includeSubtree, subtreeDepth, properties, refresh });
	}

	[McpServerTool(Name = "deepflow_get_properties"), Description("Get properties for one visual tree node by full target ID or short ID.")]
	public static McpToolResponse GetProperties(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		DeepFlowResourceStore resources,
		IOptions<DeepFlowMcpOptions> options,
		string targetId,
		[Description("Comma-separated property names, 'default', or 'none'.")] string? properties = null,
		bool refresh = false)
	{
		return runner.Run(() =>
		{
			var propertyNames = McpArgumentParsing.ParseProperties(properties, options.Value.DefaultProperties);
			var snapshot = cache.GetOrRefresh(host, propertyNames, options.Value.TreeLimit, refresh: refresh);
			var node = new NodeSnapshotService().GetProps(snapshot, new NodeSnapshotOptions
			{
				TargetId = targetId,
				IncludePath = true,
				UseShortIds = true,
				Properties = propertyNames,
			});
			resources.StoreJson(DeepFlowResourceNames.LatestNode, node);
			return node;
		}, new { targetId, properties, refresh });
	}

	[McpServerTool(Name = "deepflow_suggest_selectors"), Description("Suggest stable selector arguments for one visual tree node.")]
	public static McpToolResponse SuggestSelectors(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string targetId,
		bool refresh = false)
	{
		return runner.Run(() =>
		{
			var snapshot = cache.GetOrRefresh(host, options.Value.DefaultProperties, options.Value.TreeLimit, refresh: refresh);
			var fullId = new TargetIdService().Resolve(targetId, snapshot);
			var node = snapshot.Nodes.First(node => node.TargetId == fullId);
			List<McpSelectorSuggestion> suggestions = [];
			AddSuggestion(suggestions, snapshot, node, KnownProperties.AutomationId, 0.98, "high", static value => new McpSemanticSelector { AutomationId = value }, "Automation ID is usually the most stable selector.");
			AddSuggestion(suggestions, snapshot, node, KnownProperties.AutomationName, 0.90, "high", static value => new McpSemanticSelector { Name = value }, "Automation name is intended for UI automation.");
			AddSuggestion(suggestions, snapshot, node, KnownProperties.Name, 0.85, "medium", static value => new McpSemanticSelector { Name = value }, "Framework name is useful when automation ID is absent.");
			AddSuggestion(suggestions, snapshot, node, KnownProperties.Uid, 0.80, "medium", static value => new McpSemanticSelector { PropertyEquals = new McpPropertyMatch { Name = KnownProperties.Uid, Value = value } }, "Framework Uid can be stable in localized apps.");
			foreach (var propertyName in KnownProperties.TextualIdentityPropertyNames)
			{
				if (!TryPropertyText(node, propertyName, out var value))
					continue;

				var selector = new McpSemanticSelector { Text = value };
				suggestions.Add(new McpSelectorSuggestion
				{
					Selector = selector,
					Confidence = 0.75,
					Stability = "low",
					Unique = CountMatches(snapshot, selector.ToAutomationSelector()) == 1,
					Explanation = $"{propertyName} is a readable fallback when automation properties are absent.",
				});
				break;
			}

			suggestions.Add(new McpSelectorSuggestion
			{
				Selector = new McpTargetIdSelector { TargetId = node.TargetId },
				Confidence = 0.40,
				Stability = "revision",
				Unique = true,
				Explanation = "Runtime target IDs are exact but may become stale after UI changes.",
			});
			return new McpSelectorSuggestionsResult { TargetId = node.TargetId, Suggestions = suggestions };
		}, new { targetId, refresh });
	}

	[McpServerTool(Name = "deepflow_wait_for_element"), Description("Poll the visual tree until an element selector matches.")]
	public static Task<McpToolResponse> WaitForElement(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		IOptions<DeepFlowMcpOptions> options,
		string? typeName = null,
		string? typeContains = null,
		string? name = null,
		string? automationId = null,
		string? text = null,
		string? property = null,
		bool? visible = null,
		bool? enabled = null,
		int matchCount = 1,
		int? timeoutMs = null,
		int intervalMs = TimeoutDefaults.CliWaitIntervalMs,
		string? properties = null,
		CancellationToken cancellationToken = default)
	{
		return runner.RunAsync(async token =>
		{
			var timeout = Math.Max(1, timeoutMs ?? options.Value.DefaultTimeoutMs);
			if (matchCount <= 0)
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait match count must be greater than zero.");
			var propertyNames = McpArgumentParsing.ParseProperties(properties, options.Value.DefaultProperties);
			var findOptions = new FindSnapshotOptions
			{
				TypeName = typeName,
				TypeContains = typeContains,
				Name = name,
				AutomationId = automationId,
				Text = text,
				PropertyEquals = McpArgumentParsing.ParsePair(property, nameof(property)),
				Visible = visible,
				Enabled = enabled,
				Limit = Math.Max(matchCount, 50),
				IncludePath = true,
				IncludeProperties = true,
				Properties = propertyNames,
				UseShortIds = true,
			};

			var session = host.RequireSession();
			var result = await new WaitEngine().WaitAsync(
				session.AppSession,
				new WaitRequest(
					new ElementMinimumCountWaitCondition(new FindOptionsWaitTargetMatcher(findOptions), matchCount),
					new McpWaitObservationSource(session, cache),
					timeout,
					intervalMs,
					new WaitSnapshotRequest(propertyNames, options.Value.TreeLimit)),
				token).ConfigureAwait(false);
			return result.MatchResult!;
		}, new
		{
			typeName,
			typeContains,
			name,
			automationId,
			text,
			property,
			visible,
			enabled,
			matchCount,
			timeoutMs,
			intervalMs,
			properties,
		}, cancellationToken);
	}

	[McpServerTool(Name = "deepflow_get_binding_failures"), Description("Read WPF binding failures captured by the attached target payload.")]
	public static McpToolResponse GetBindingFailures(
		McpToolRunner runner,
		McpSessionHost host,
		DeepFlowResourceStore resources,
		IOptions<DeepFlowMcpOptions> options,
		long? afterSequenceNumber = null,
		int maxCount = 1000)
	{
		return runner.Run(() =>
		{
			var failures = host.Send<BindingFailureBatchDto>(
				new GetBindingFailuresCommandRequest(afterSequenceNumber, maxCount, options.Value.DefaultTimeoutMs),
				options.Value.DefaultTimeoutMs);
			resources.StoreJson(DeepFlowResourceNames.LatestBindingFailures, failures);
			return failures;
		}, new { afterSequenceNumber, maxCount });
	}

	private static IReadOnlyList<string> EnsureVisibilityProperty(IReadOnlyList<string> properties, bool includeHidden)
	{
		if (includeHidden || properties.Contains(KnownProperties.IsVisible, StringComparer.Ordinal))
			return properties;

		return [.. properties, KnownProperties.IsVisible];
	}

	private static bool IsCondensedOutput(string? outputFormat)
	{
		var normalized = string.IsNullOrWhiteSpace(outputFormat)
			? McpSemanticRecordingFormatter.FormatName
			: outputFormat.Trim().ToLowerInvariant();
		return normalized switch
		{
			McpSemanticRecordingFormatter.FormatName or "condensed" or "text" => true,
			"json" or "tree-json" => false,
			_ => throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Unsupported outputFormat '{outputFormat}'."),
		};
	}

	private static void AddSuggestion(
		List<McpSelectorSuggestion> suggestions,
		VisualTreeSnapshot snapshot,
		VisualTreeNodeDto node,
		string propertyName,
		double confidence,
		string stability,
		Func<string, McpAgentSelector> createSelector,
		string explanation)
	{
		if (!TryPropertyText(node, propertyName, out var value))
			return;

		var selector = createSelector(value);
		suggestions.Add(new McpSelectorSuggestion
		{
			Selector = selector,
			Confidence = confidence,
			Stability = stability,
			Unique = CountMatches(snapshot, selector.ToAutomationSelector()) == 1,
			Explanation = explanation,
		});
	}

	private static int CountMatches(VisualTreeSnapshot snapshot, ElementSelector selector) =>
		new FindSnapshotService().Find(snapshot, new FindSnapshotOptions
		{
			TypeName = selector.TypeName,
			Name = selector.Name,
			AutomationId = selector.AutomationId,
			Text = selector.Text,
			PropertyEquals = selector.PropertyEquals,
			Limit = 2,
			IncludeProperties = false,
			UseShortIds = true,
		}).MatchCount;

	private static bool TryPropertyText(VisualTreeNodeDto node, string propertyName, out string value)
	{
		value = node.Properties.TryGetValue(propertyName, out var raw)
			? Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
			: string.Empty;
		return value.Length > 0;
	}
}
