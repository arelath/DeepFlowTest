namespace DeepFlowTest.Mcp.Resources;

using System;
using System.ComponentModel;
using System.Text.Json;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

[McpServerResourceType]
internal static class DeepFlowResources
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
	};

	[McpServerResource(Name = "deepflow_target_status", UriTemplate = DeepFlowResourceNames.TargetStatus, MimeType = "application/json")]
	[Description("Current attached target status.")]
	public static string TargetStatus(IServiceProvider services)
	{
		var host = services.GetRequiredService<McpSessionHost>();
		return JsonSerializer.Serialize(host.Status, JsonOptions);
	}

	[McpServerResource(Name = "deepflow_live_visual_tree", UriTemplate = DeepFlowResourceNames.LiveVisualTree, MimeType = "application/json")]
	[Description("Fresh visual tree read from the attached target with default MCP properties.")]
	public static string LiveVisualTree(IServiceProvider services)
	{
		var host = services.GetRequiredService<McpSessionHost>();
		var cache = services.GetRequiredService<McpSnapshotCache>();
		var options = services.GetRequiredService<IOptions<DeepFlowMcpOptions>>().Value;
		var store = services.GetRequiredService<DeepFlowResourceStore>();
		var snapshot = cache.GetOrRefresh(host, options.DefaultProperties, options.TreeLimit, refresh: true);
		var tree = new TreeSnapshotService().Shape(snapshot, new TreeSnapshotOptions
		{
			Shape = TreeShape.Flat,
			Limit = options.TreeLimit,
			IncludeHidden = false,
			IncludeTypeNames = true,
			IncludePath = true,
			Properties = options.DefaultProperties,
			UseShortIds = true,
		});
		store.StoreJson(DeepFlowResourceNames.LatestVisualTree, tree);
		return JsonSerializer.Serialize(tree, JsonOptions);
	}

	[McpServerResource(Name = "deepflow_node_details", UriTemplate = "deepflow://node/{targetId}", MimeType = "application/json")]
	[Description("Fresh node details for a target ID or short ID.")]
	public static string NodeDetails(IServiceProvider services, string targetId)
	{
		var host = services.GetRequiredService<McpSessionHost>();
		var cache = services.GetRequiredService<McpSnapshotCache>();
		var options = services.GetRequiredService<IOptions<DeepFlowMcpOptions>>().Value;
		var store = services.GetRequiredService<DeepFlowResourceStore>();
		var snapshot = cache.GetOrRefresh(host, options.DefaultProperties, options.TreeLimit, refresh: false);
		var node = new NodeSnapshotService().GetNode(snapshot, new NodeSnapshotOptions
		{
			TargetId = targetId,
			IncludeAncestors = true,
			IncludeChildren = true,
			IncludePath = true,
			UseShortIds = true,
			Properties = options.DefaultProperties,
		});
		store.StoreJson(DeepFlowResourceNames.LatestNode, node);
		return JsonSerializer.Serialize(node, JsonOptions);
	}

	[McpServerResource(Name = "deepflow_latest_visual_tree", UriTemplate = DeepFlowResourceNames.LatestVisualTree, MimeType = "application/json")]
	[Description("Most recent visual tree captured by deepflow_get_visual_tree or deepflow_live_visual_tree.")]
	public static string LatestVisualTree(IServiceProvider services) =>
		services.GetRequiredService<DeepFlowResourceStore>().ReadText(DeepFlowResourceNames.LatestVisualTree);

	[McpServerResource(Name = "deepflow_latest_node", UriTemplate = DeepFlowResourceNames.LatestNode, MimeType = "application/json")]
	[Description("Most recent node details captured by deepflow_get_node, deepflow_get_properties, or deepflow_node_details.")]
	public static string LatestNode(IServiceProvider services) =>
		services.GetRequiredService<DeepFlowResourceStore>().ReadText(DeepFlowResourceNames.LatestNode);

	[McpServerResource(Name = "deepflow_latest_screenshot", UriTemplate = DeepFlowResourceNames.LatestScreenshot, MimeType = "application/json")]
	[Description("Most recent screenshot metadata and base64 bytes captured by deepflow_capture_screenshot.")]
	public static string LatestScreenshot(IServiceProvider services) =>
		services.GetRequiredService<DeepFlowResourceStore>().ReadText(DeepFlowResourceNames.LatestScreenshot);

	[McpServerResource(Name = "deepflow_latest_binding_failures", UriTemplate = DeepFlowResourceNames.LatestBindingFailures, MimeType = "application/json")]
	[Description("Most recent binding failure batch captured by deepflow_get_binding_failures.")]
	public static string LatestBindingFailures(IServiceProvider services) =>
		services.GetRequiredService<DeepFlowResourceStore>().ReadText(DeepFlowResourceNames.LatestBindingFailures);

	[McpServerResource(Name = "deepflow_recent_logs", UriTemplate = DeepFlowResourceNames.RecentLogs, MimeType = "application/json")]
	[Description("Recent MCP tool failures captured in memory.")]
	public static string RecentLogs(IServiceProvider services) =>
		services.GetRequiredService<DeepFlowResourceStore>().ReadText(DeepFlowResourceNames.RecentLogs);
}
