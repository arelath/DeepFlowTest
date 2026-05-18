namespace DeepFlowTest.Mcp.Tools;

using System;
using System.ComponentModel;
using DeepFlowTest;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

[McpServerToolType]
internal static class StreamTools
{
	[McpServerTool(Name = "deepflow_start_stream"), Description("Start a bounded MCP-side stream buffer for visual tree, screenshot, event-log, or binding-failures frames.")]
	public static McpToolResponse StartStream(
		McpToolRunner runner,
		McpSessionHost host,
		McpStreamRegistry streams,
		IOptions<DeepFlowMcpOptions> options,
		[Description("Stream kind: visual-tree, visual-tree-delta, screenshot, event-log, or binding-failures.")] string kind = ProtocolConstants.StreamKinds.VisualTree,
		int intervalMs = TimeoutDefaults.StreamIntervalMs,
		string? properties = null,
		string? targetId = null,
		string? imageFormat = "png",
		int? timeoutMs = null)
	{
		return runner.Run(() =>
		{
			if (!IsKnownStreamKind(kind))
				throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported stream kind '{kind}'.");
			if (intervalMs < TimeoutDefaults.StreamMinimumIntervalMs)
				throw new CliException(CliErrorCodes.InvalidArguments, $"intervalMs must be at least {TimeoutDefaults.StreamMinimumIntervalMs}.");

			var timeout = Math.Max(1, timeoutMs ?? options.Value.DefaultTimeoutMs);
			var request = new StartSendingCommandRequest
			{
				StreamKind = kind,
				IntervalMs = intervalMs,
				PropNames = McpArgumentParsing.ParseProperties(properties, options.Value.DefaultProperties),
				TargetId = targetId,
				Format = McpArgumentParsing.ParseImageFormat(imageFormat, ImageFormat.Png),
				TimeoutMs = timeout,
			};
			return streams.Start(host.RequireSession(), request, timeout);
		});
	}

	[McpServerTool(Name = "deepflow_read_stream"), Description("Read buffered frames from a stream started by deepflow_start_stream.")]
	public static McpToolResponse ReadStream(
		McpToolRunner runner,
		McpStreamRegistry streams,
		string streamId,
		int maxFrames = 10)
	{
		return runner.Run(() => streams.Read(streamId, maxFrames));
	}

	[McpServerTool(Name = "deepflow_stop_stream"), Description("Stop and dispose a stream started by deepflow_start_stream.")]
	public static McpToolResponse StopStream(
		McpToolRunner runner,
		McpStreamRegistry streams,
		string streamId)
	{
		return runner.Run(() => streams.Stop(streamId));
	}

	private static bool IsKnownStreamKind(string kind) =>
		kind is ProtocolConstants.StreamKinds.VisualTree
			or ProtocolConstants.StreamKinds.VisualTreeDelta
			or ProtocolConstants.StreamKinds.Screenshot
			or ProtocolConstants.StreamKinds.EventLog
			or ProtocolConstants.StreamKinds.BindingFailures;
}
