namespace DeepFlowTest.Mcp.Tools;

using System;
using System.ComponentModel;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

[McpServerToolType]
internal static class DiagnosticsTools
{
	[McpServerTool(Name = "deepflow_configure_diagnostics"), Description("Configure target-side diagnostics such as the virtual pointer overlay.")]
	public static McpToolResponse ConfigureDiagnostics(
		McpToolRunner runner,
		McpSessionHost host,
		IOptions<DeepFlowMcpOptions> options,
		IMcpActivitySink activity,
		[Description("Enable or disable the target-side virtual pointer overlay.")] bool? virtualPointerEnabled = null,
		[Description("Show click ripples when the virtual pointer reports clicks.")] bool? showClickRipples = null,
		[Description("Show a drag trail while the virtual pointer reports drag activity.")] bool? showDragTrail = null,
		[Description("Delay before the virtual pointer hides after activity.")] int? hideDelayMs = null,
		[Description("Include the virtual pointer overlay in screenshots when possible.")] bool? includeInScreenshots = null,
		[Description("Command timeout in milliseconds.")] int? timeoutMs = null)
	{
		return runner.Run(() =>
		{
			var timeout = Math.Max(1, timeoutMs ?? options.Value.DefaultTimeoutMs);
			var virtualPointer = new VirtualPointerOptionsDto
			{
				Enabled = virtualPointerEnabled ?? false,
				ShowClickRipples = showClickRipples ?? true,
				ShowDragTrail = showDragTrail ?? true,
				HideDelayMs = Math.Max(0, hideDelayMs ?? 800),
				IncludeInScreenshots = includeInScreenshots ?? false,
			};

			var response = host.Send<object>(
				new ConfigureDiagnosticsCommandRequest
				{
					TimeoutMs = timeout,
					VirtualPointer = virtualPointer,
				},
				timeout);

			activity.Publish(new McpActivityEvent
			{
				Source = "server",
				Kind = "diagnostics.configure",
				Name = "virtual-pointer",
				Status = "success",
				Details = virtualPointer,
			});

			return new DiagnosticsConfigurationResult
			{
				VirtualPointer = virtualPointer,
				Payload = response,
			};
		}, new { virtualPointerEnabled, showClickRipples, showDragTrail, hideDelayMs, includeInScreenshots, timeoutMs });
	}
}

internal sealed record class DiagnosticsConfigurationResult
{
	public VirtualPointerOptionsDto VirtualPointer { get; init; } = new();

	public object? Payload { get; init; }
}
