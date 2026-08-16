namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Resources;
using Microsoft.Extensions.Logging;

internal sealed class McpToolRunner
{
	private readonly McpSessionHost sessionHost;
	private readonly DeepFlowResourceStore resources;
	private readonly ILogger<McpToolRunner> logger;
	private readonly IMcpActivitySink? activity;

	public McpToolRunner(McpSessionHost sessionHost, DeepFlowResourceStore resources, ILogger<McpToolRunner> logger)
		: this(sessionHost, resources, logger, activity: null)
	{
	}

	public McpToolRunner(McpSessionHost sessionHost, DeepFlowResourceStore resources, ILogger<McpToolRunner> logger, IMcpActivitySink? activity)
	{
		this.sessionHost = sessionHost ?? throw new ArgumentNullException(nameof(sessionHost));
		this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
		this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
		this.activity = activity;
	}

	public McpToolResponse Run(Func<object?> action, object? parameters = null, [CallerMemberName] string toolName = "")
	{
		ArgumentNullException.ThrowIfNull(action);
		var stopwatch = Stopwatch.StartNew();
		activity?.Publish(new McpActivityEvent
		{
			Source = "client",
			Kind = "tool.start",
			Name = toolName,
			Status = "started",
			Details = new ToolActivityDetails { Parameters = parameters },
		});
		try
		{
			var result = action();
			var response = McpToolResponse.Ok(result, sessionHost.Status);
			activity?.Publish(new McpActivityEvent
			{
				Source = "client",
				Kind = "tool.success",
				Name = toolName,
				Status = "success",
				Duration = stopwatch.Elapsed,
				Details = new ToolActivityDetails
				{
					Parameters = parameters,
					Result = result,
				},
			});
			return response;
		}
		catch (AutomationException ex)
		{
			resources.AddLog("warning", ex.ErrorCode, ex.Message, GetContextId(parameters));
			PublishFailure(toolName, stopwatch.Elapsed, ex.ErrorCode, ex.Message, parameters, ex.Details);
			return McpToolResponse.Fail(ex.ErrorCode, ex.Message, ex.Details, RecoveryFor(ex.ErrorCode), sessionHost.Status);
		}
		catch (NamedPipeSessionException ex)
		{
			var errorCode = ProtocolErrorMapper.Map(ex.ErrorCode);
			resources.AddLog("warning", errorCode, ex.Message, GetContextId(parameters));
			PublishFailure(toolName, stopwatch.Elapsed, errorCode, ex.Message, parameters, new { protocolErrorCode = ex.ErrorCode, ex.TargetExitCode, ex.CrashLog });
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
			resources.AddLog("warning", errorCode, ex.Message, GetContextId(parameters));
			PublishFailure(toolName, stopwatch.Elapsed, errorCode, ex.Message, parameters, new { protocolErrorCode = ex.ErrorCode });
			return McpToolResponse.Fail(errorCode, ex.Message, new { protocolErrorCode = ex.ErrorCode }, RecoveryFor(errorCode), sessionHost.Status);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			logger.LogError(ex, "MCP tool failed.");
			resources.AddLog("error", AutomationErrorCodes.UnexpectedError, "Unexpected MCP tool failure. See stderr logs for details.", GetContextId(parameters));
			PublishFailure(toolName, stopwatch.Elapsed, AutomationErrorCodes.UnexpectedError, "Unexpected MCP tool failure. See stderr logs for details.", parameters, new
			{
				exceptionType = ex.GetType().FullName,
				exceptionMessage = ex.Message,
			});
			return McpToolResponse.Fail(AutomationErrorCodes.UnexpectedError, "Unexpected MCP tool failure. See stderr logs for details.", recovery: "Check the MCP server stderr log for details.", target: sessionHost.Status);
		}
	}

	public async Task<McpToolResponse> RunAsync<T>(
		Func<CancellationToken, Task<T>> action,
		object? parameters = null,
		CancellationToken cancellationToken = default,
		[CallerMemberName] string toolName = "")
	{
		ArgumentNullException.ThrowIfNull(action);
		var stopwatch = Stopwatch.StartNew();
		activity?.Publish(new McpActivityEvent
		{
			Source = "client",
			Kind = "tool.start",
			Name = toolName,
			Status = "started",
			Details = new ToolActivityDetails { Parameters = parameters },
		});
		try
		{
			var result = await action(cancellationToken).ConfigureAwait(false);
			var response = McpToolResponse.Ok(result, sessionHost.Status);
			activity?.Publish(new McpActivityEvent
			{
				Source = "client",
				Kind = "tool.success",
				Name = toolName,
				Status = "success",
				Duration = stopwatch.Elapsed,
				Details = new ToolActivityDetails
				{
					Parameters = parameters,
					Result = result,
				},
			});
			return response;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			activity?.Publish(new McpActivityEvent
			{
				Source = "client",
				Kind = "tool.canceled",
				Name = toolName,
				Status = "canceled",
				Duration = stopwatch.Elapsed,
				Details = new ToolActivityDetails { Parameters = parameters },
			});
			throw;
		}
		catch (AutomationException ex)
		{
			resources.AddLog("warning", ex.ErrorCode, ex.Message, GetContextId(parameters));
			PublishFailure(toolName, stopwatch.Elapsed, ex.ErrorCode, ex.Message, parameters, ex.Details);
			return McpToolResponse.Fail(ex.ErrorCode, ex.Message, ex.Details, RecoveryFor(ex.ErrorCode), sessionHost.Status);
		}
		catch (NamedPipeSessionException ex)
		{
			var errorCode = ProtocolErrorMapper.Map(ex.ErrorCode);
			resources.AddLog("warning", errorCode, ex.Message, GetContextId(parameters));
			PublishFailure(toolName, stopwatch.Elapsed, errorCode, ex.Message, parameters, new { protocolErrorCode = ex.ErrorCode, ex.TargetExitCode, ex.CrashLog });
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
			resources.AddLog("warning", errorCode, ex.Message, GetContextId(parameters));
			PublishFailure(toolName, stopwatch.Elapsed, errorCode, ex.Message, parameters, new { protocolErrorCode = ex.ErrorCode });
			return McpToolResponse.Fail(errorCode, ex.Message, new { protocolErrorCode = ex.ErrorCode }, RecoveryFor(errorCode), sessionHost.Status);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			logger.LogError(ex, "MCP tool failed.");
			resources.AddLog("error", AutomationErrorCodes.UnexpectedError, "Unexpected MCP tool failure. See stderr logs for details.", GetContextId(parameters));
			PublishFailure(toolName, stopwatch.Elapsed, AutomationErrorCodes.UnexpectedError, "Unexpected MCP tool failure. See stderr logs for details.", parameters, new
			{
				exceptionType = ex.GetType().FullName,
				exceptionMessage = ex.Message,
			});
			return McpToolResponse.Fail(AutomationErrorCodes.UnexpectedError, "Unexpected MCP tool failure. See stderr logs for details.", recovery: "Check the MCP server stderr log for details.", target: sessionHost.Status);
		}
	}

	private static string? GetContextId(object? parameters)
	{
		if (parameters is IReadOnlyDictionary<string, object?> readOnly
			&& readOnly.TryGetValue("contextId", out var dictionaryValue))
			return dictionaryValue as string;

		return parameters?.GetType()
			.GetProperty("contextId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
			?.GetValue(parameters) as string;
	}

	private void PublishFailure(string toolName, TimeSpan duration, string errorCode, string message, object? parameters, object? errorDetails) =>
		activity?.Publish(new McpActivityEvent
		{
			Source = "client",
			Kind = "tool.failure",
			Name = toolName,
			Status = "failure",
			Duration = duration,
			Summary = message,
			Details = new ToolActivityDetails
			{
				Parameters = parameters,
				Error = new
				{
					code = errorCode,
					message,
					details = errorDetails,
				},
			},
		});

	private static string? RecoveryFor(string errorCode) =>
		errorCode switch
		{
			AutomationErrorCodes.InvalidArguments => "Check the tool arguments and retry.",
			AutomationErrorCodes.TargetNotFound => "Call deepflow_list_processes, then attach to a live PID or exact process name.",
			AutomationErrorCodes.AmbiguousTarget => "Use a PID or a more specific process/window selector.",
			AutomationErrorCodes.TargetExited => "Launch or attach to a live target before retrying.",
			AutomationErrorCodes.PipeFailed => "Retry ping. If the target is alive, detach and attach again.",
			AutomationErrorCodes.ProtocolError => "Refresh the target status. If the failure repeats, detach and attach again.",
			AutomationErrorCodes.CommandTimeout => "Increase timeoutMs or wait for the target UI thread to become responsive.",
			AutomationErrorCodes.StaleTarget => "Refresh the visual tree and resolve the element again.",
			AutomationErrorCodes.UnsupportedTarget => "Inspect the visual tree and choose a target that supports the requested operation.",
			AutomationErrorCodes.ActionDenied => "Start the MCP server with allowActions or allowLaunch when that operation is intended.",
			AutomationErrorCodes.ArbitraryInvokeDenied => "Use a known operation, or explicitly enable arbitrary invoke policy only in a trusted session.",
			_ => null,
		};
}
