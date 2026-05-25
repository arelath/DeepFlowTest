namespace DeepFlowTest.Mcp.Tools;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.Resources;
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
		});
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
		});
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
		});
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
			var fullId = new CliTargetIdService().Resolve(targetId, snapshot);
			var node = snapshot.Nodes.First(node => node.TargetId == fullId);
			return new SelectorSuggestionService(new CliTargetIdService(), snapshot).Suggest(node, useShortIds: true);
		});
	}

	[McpServerTool(Name = "deepflow_wait_for_element"), Description("Poll the visual tree until an element selector matches.")]
	public static McpToolResponse WaitForElement(
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
		string? properties = null)
	{
		return runner.Run(() =>
		{
			if (matchCount <= 0)
				throw new CliException(CliErrorCodes.InvalidArguments, "matchCount must be greater than zero.");
			if (intervalMs <= 0)
				throw new CliException(CliErrorCodes.InvalidArguments, "intervalMs must be greater than zero.");

			var timeout = Math.Max(1, timeoutMs ?? options.Value.DefaultTimeoutMs);
			var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeout);
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

			while (DateTimeOffset.UtcNow <= deadline)
			{
				var snapshot = cache.GetOrRefresh(host, propertyNames, options.Value.TreeLimit, refresh: true);
				var result = new FindSnapshotService().Find(snapshot, findOptions);
				if (result.MatchCount >= matchCount)
					return result;

				Thread.Sleep(Math.Min(intervalMs, Math.Max(1, (int)(deadline - DateTimeOffset.UtcNow).TotalMilliseconds)));
			}

			throw new CliException(CliErrorCodes.CommandTimeout, $"Wait timed out after {timeout} ms.");
		});
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
		});
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
			_ => throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported outputFormat '{outputFormat}'."),
		};
	}
}
