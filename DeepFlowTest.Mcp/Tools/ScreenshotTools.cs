namespace DeepFlowTest.Mcp.Tools;

using System.ComponentModel;
using System.Linq;
using DeepFlowTest;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.Resources;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

[McpServerToolType]
internal static class ScreenshotTools
{
	[McpServerTool(Name = "deepflow_capture_screenshot"), Description("Capture a screenshot of the attached target or a resolved element.")]
	public static McpToolResponse CaptureScreenshot(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		DeepFlowResourceStore resources,
		IOptions<DeepFlowMcpOptions> options,
		string? targetId = null,
		string? typeName = null,
		string? name = null,
		string? automationId = null,
		string? text = null,
		string? property = null,
		string? imageFormat = "png",
		bool includeBase64 = false,
		string? outputPath = null)
	{
		return runner.Run(() =>
		{
			if (!string.IsNullOrWhiteSpace(outputPath) && !options.Value.Policy.AllowFileWrites)
				throw new AutomationException(AutomationErrorCodes.ActionDenied, "Writing screenshots to disk requires allowFileWrites policy.");

			var resolvedTargetId = ResolveTargetId(host, cache, options.Value, targetId, typeName, name, automationId, text, property);
			var response = host.Send<ScreenshotCommandResponse>(
				new ScreenshotCommandRequest
				{
					Format = McpArgumentParsing.ParseImageFormat(imageFormat, ImageFormat.Png),
					TargetId = resolvedTargetId,
					TimeoutMs = options.Value.DefaultTimeoutMs,
				},
				options.Value.DefaultTimeoutMs);
			var screenshot = new ScreenshotFileService().Process(response, new ScreenshotFileOptions
			{
				OutputPath = outputPath,
				IncludeBase64 = includeBase64,
			});
			var resource = resources.StoreScreenshot(response);
			return new ScreenshotCaptureData(screenshot, resource);
		}, new { targetId, typeName, name, automationId, text, property, imageFormat, includeBase64, outputPath });
	}

	private static string? ResolveTargetId(
		McpSessionHost host,
		McpSnapshotCache cache,
		DeepFlowMcpOptions options,
		string? targetId,
		string? typeName,
		string? name,
		string? automationId,
		string? text,
		string? property)
	{
		if (string.IsNullOrWhiteSpace(targetId)
			&& string.IsNullOrWhiteSpace(typeName)
			&& string.IsNullOrWhiteSpace(name)
			&& string.IsNullOrWhiteSpace(automationId)
			&& string.IsNullOrWhiteSpace(text)
			&& string.IsNullOrWhiteSpace(property))
			return null;

		if (!string.IsNullOrWhiteSpace(targetId))
		{
			if (LooksLikeFullTargetId(targetId!))
				return targetId;

			var targetSnapshot = cache.GetOrRefresh(host, options.DefaultProperties, options.TreeLimit, refresh: false);
			return new TargetIdService().Resolve(targetId!, targetSnapshot);
		}

		var snapshot = cache.GetOrRefresh(host, options.DefaultProperties, options.TreeLimit, refresh: false);
		var resolution = new ElementResolver().Resolve(snapshot, new DeepFlowTest.Automation.ElementSelector
		{
			TypeName = typeName,
			Name = name,
			AutomationId = automationId,
			Text = text,
			PropertyEquals = McpArgumentParsing.ParsePair(property, nameof(property)),
			Visible = true,
			First = false,
		});
		return resolution.TargetId;
	}

	private static bool LooksLikeFullTargetId(string targetId) =>
		targetId.Length > 8 && targetId.Contains('-', System.StringComparison.Ordinal);
}

internal sealed record class ScreenshotCaptureData(ScreenshotResultData Screenshot, DeepFlowResourceReference Resource);
