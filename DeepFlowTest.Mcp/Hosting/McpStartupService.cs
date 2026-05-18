namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class McpStartupService : IHostedService
{
	private readonly McpSessionHost sessionHost;
	private readonly IOptions<McpServerOptions> options;
	private readonly ILogger<McpStartupService> logger;

	public McpStartupService(
		McpSessionHost sessionHost,
		IOptions<McpServerOptions> options,
		ILogger<McpStartupService> logger)
	{
		this.sessionHost = sessionHost ?? throw new ArgumentNullException(nameof(sessionHost));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		var startup = options.Value.Startup;
		try
		{
			if (startup.HasLaunchRequest)
			{
				sessionHost.Launch(new McpLaunchOptions
				{
					FileName = startup.LaunchPath!,
					Arguments = startup.LaunchArguments,
					WorkingDirectory = startup.WorkingDirectory,
					AttachTimeoutMs = options.Value.AttachTimeoutMs,
					TerminateOnDetach = startup.TerminateOnDetach,
				});
			}
			else if (startup.HasAttachSelector)
			{
				sessionHost.Attach(
					new McpTargetSelector
					{
						ProcessId = startup.ProcessId,
						ProcessName = startup.ProcessName,
						WindowTitle = startup.WindowTitle,
					},
					options.Value.AttachTimeoutMs,
					startup.NoInject,
					startup.PipeId);
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			logger.LogError(ex, "Initial MCP target acquisition failed. The server will stay online unattached.");
		}

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		sessionHost.Detach();
		return Task.CompletedTask;
	}
}
