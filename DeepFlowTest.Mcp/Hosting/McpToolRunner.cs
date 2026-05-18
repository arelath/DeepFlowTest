namespace DeepFlowTest.Mcp.Hosting;

using System;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Resources;
using Microsoft.Extensions.Logging;

internal sealed class McpToolRunner
{
	private readonly McpSessionHost sessionHost;
	private readonly DeepFlowResourceStore resources;
	private readonly ILogger<McpToolRunner> logger;

	public McpToolRunner(McpSessionHost sessionHost, DeepFlowResourceStore resources, ILogger<McpToolRunner> logger)
	{
		this.sessionHost = sessionHost ?? throw new ArgumentNullException(nameof(sessionHost));
		this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
		this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public McpToolResponse Run(Func<object?> action)
	{
		ArgumentNullException.ThrowIfNull(action);
		try
		{
			return McpToolResponse.Ok(action(), sessionHost.Status);
		}
		catch (CliException ex)
		{
			resources.AddLog("warning", ex.ErrorCode, ex.Message);
			return McpToolResponse.Fail(ex.ErrorCode, ex.Message, ex.Details, RecoveryFor(ex.ErrorCode), sessionHost.Status);
		}
		catch (NamedPipeSessionException ex)
		{
			var errorCode = ProtocolErrorMapper.Map(ex.ErrorCode);
			resources.AddLog("warning", errorCode, ex.Message);
			return McpToolResponse.Fail(
				errorCode,
				ex.Message,
				new { protocolErrorCode = ex.ErrorCode, ex.TargetExitCode, ex.CrashLog },
				RecoveryFor(errorCode),
				sessionHost.Status);
		}
		catch (ProtocolException ex)
		{
			var errorCode = ProtocolErrorMapper.Map(ex.ErrorCode);
			resources.AddLog("warning", errorCode, ex.Message);
			return McpToolResponse.Fail(errorCode, ex.Message, new { protocolErrorCode = ex.ErrorCode }, RecoveryFor(errorCode), sessionHost.Status);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			logger.LogError(ex, "MCP tool failed.");
			resources.AddLog("error", CliErrorCodes.UnexpectedError, "Unexpected MCP tool failure. See stderr logs for details.");
			return McpToolResponse.Fail(CliErrorCodes.UnexpectedError, "Unexpected MCP tool failure. See stderr logs for details.", recovery: "Check the MCP server stderr log for details.", target: sessionHost.Status);
		}
	}

	private static string? RecoveryFor(string errorCode) =>
		errorCode switch
		{
			CliErrorCodes.InvalidArguments => "Check the tool arguments and retry.",
			CliErrorCodes.TargetNotFound => "Call deepflow_list_processes, then attach to a live PID or exact process name.",
			CliErrorCodes.AmbiguousTarget => "Use a PID or a more specific process/window selector.",
			CliErrorCodes.TargetExited => "Launch or attach to a live target before retrying.",
			CliErrorCodes.PipeFailed => "Retry ping. If the target is alive, detach and attach again.",
			CliErrorCodes.ProtocolError => "Refresh the target status. If the failure repeats, detach and attach again.",
			CliErrorCodes.CommandTimeout => "Increase timeoutMs or wait for the target UI thread to become responsive.",
			CliErrorCodes.StaleTarget => "Refresh the visual tree and resolve the element again.",
			CliErrorCodes.UnsupportedTarget => "Inspect the visual tree and choose a target that supports the requested operation.",
			CliErrorCodes.ActionDenied => "Start the MCP server with allowActions or allowLaunch when that operation is intended.",
			CliErrorCodes.ArbitraryInvokeDenied => "Use a known operation, or explicitly enable arbitrary invoke policy only in a trusted session.",
			_ => null,
		};
}
