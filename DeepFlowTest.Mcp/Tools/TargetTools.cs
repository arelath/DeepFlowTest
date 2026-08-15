namespace DeepFlowTest.Mcp.Tools;

using System;
using System.ComponentModel;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

[McpServerToolType]
internal static class TargetTools
{
	[McpServerTool(Name = "deepflow_list_processes"), Description("List local desktop processes that DeepFlowTest may be able to inspect or automate.")]
	public static McpToolResponse ListProcesses(
		McpToolRunner runner,
		AutomationServices services,
		[Description("When true, return only likely UI automation candidates.")] bool candidatesOnly = true)
	{
		return runner.Run(() =>
		{
			var result = services.ProcessSnapshotSource.GetSnapshots();
			return ProcessListData.FromSnapshotResult(
				result,
				candidatesOnly,
				excludeExited: true);
		}, new { candidatesOnly });
	}

	[McpServerTool(Name = "deepflow_attach_target"), Description("Attach DeepFlowTest to an existing process by PID, process name, or window title.")]
	public static McpToolResponse AttachTarget(
		McpToolRunner runner,
		McpSessionHost host,
		[Description("Target process ID.")] int? pid = null,
		[Description("Target process name, with or without .exe.")] string? process = null,
		[Description("Executable path. The file name is used as the process selector.")] string? executablePath = null,
		[Description("Substring of a target top-level window title.")] string? windowTitle = null,
		[Description("Attach timeout in milliseconds.")] int? timeoutMs = null,
		[Description("Do not inject if the reusable listener is not already running.")] bool noInject = false,
		[Description("Optional stable pipe ID.")] string? pipeId = null)
	{
		return runner.Run(() => host.Attach(
			new McpTargetSelector
			{
				ProcessId = pid,
				ProcessName = process,
				ExecutablePath = executablePath,
				WindowTitle = windowTitle,
			},
			timeoutMs,
			noInject,
			pipeId),
			new { pid, process, executablePath, windowTitle, timeoutMs, noInject, pipeId });
	}

	[McpServerTool(Name = "deepflow_launch_target"), Description("Launch an application and attach DeepFlowTest to the launched process.")]
	public static McpToolResponse LaunchTarget(
		McpToolRunner runner,
		McpSessionHost host,
		[Description("Executable path to launch.")] string fileName,
		[Description("Raw command-line arguments for the launched process.")] string? arguments = null,
		[Description("Working directory. Defaults to the executable directory.")] string? workingDirectory = null,
		[Description("Attach timeout in milliseconds.")] int? attachTimeoutMs = null,
		[Description("Terminate the launched process when the MCP target is detached or the server exits.")] bool terminateOnDetach = false)
	{
		return runner.Run(() => host.Launch(new McpLaunchOptions
		{
			FileName = fileName,
			Arguments = arguments,
			WorkingDirectory = workingDirectory,
			AttachTimeoutMs = attachTimeoutMs,
			TerminateOnDetach = terminateOnDetach,
		}), new { fileName, arguments, workingDirectory, attachTimeoutMs, terminateOnDetach });
	}

	[McpServerTool(Name = "deepflow_detach_target"), Description("Detach from the current target and stop active streams.")]
	public static McpToolResponse DetachTarget(McpToolRunner runner, McpSessionHost host) =>
		runner.Run(host.Detach, new { });

	[McpServerTool(Name = "deepflow_target_status"), Description("Return the current DeepFlowTest MCP target status.")]
	public static McpToolResponse TargetStatus(McpToolRunner runner, McpSessionHost host) =>
		runner.Run(() => host.Status, new { });

	[McpServerTool(Name = "deepflow_ping_target"), Description("Ping the attached target through the DeepFlowTest payload protocol.")]
	public static McpToolResponse PingTarget(
		McpToolRunner runner,
		McpSessionHost host,
		IOptions<DeepFlowMcpOptions> options,
		[Description("Command timeout in milliseconds.")] int? timeoutMs = null)
	{
		return runner.Run(() => host.Send<PingCommandResponse>(
			new PingCommandRequest(timeoutMs ?? options.Value.DefaultTimeoutMs),
			Math.Max(1, timeoutMs ?? options.Value.DefaultTimeoutMs)), new { timeoutMs });
	}
}
