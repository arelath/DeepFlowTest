namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

internal sealed class DeepFlowMcpHost : IAsyncDisposable
{
	private readonly SemaphoreSlim lifecycle = new(1, 1);
	private readonly IMcpHttpServerFactory serverFactory;
	private readonly McpHttpApplication application;
	private readonly IMcpTransportSessionCleaner sessionCleaner;
	private readonly McpStartupService startupService;
	private readonly IOptions<DeepFlowMcpOptions> options;
	private readonly McpEndpointReporter endpointReporter;
	private readonly IMcpActivitySink activity;
	private readonly ILogger<DeepFlowMcpHost> logger;
	private IServer? server;
	private bool disposed;

	public DeepFlowMcpHost(
		IMcpHttpServerFactory serverFactory,
		McpHttpApplication application,
		IMcpTransportSessionCleaner sessionCleaner,
		McpStartupService startupService,
		IOptions<DeepFlowMcpOptions> options,
		McpEndpointReporter endpointReporter,
		IMcpActivitySink activity,
		ILogger<DeepFlowMcpHost> logger)
	{
		this.serverFactory = serverFactory ?? throw new ArgumentNullException(nameof(serverFactory));
		this.application = application ?? throw new ArgumentNullException(nameof(application));
		this.sessionCleaner = sessionCleaner ?? throw new ArgumentNullException(nameof(sessionCleaner));
		this.startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.endpointReporter = endpointReporter ?? throw new ArgumentNullException(nameof(endpointReporter));
		this.activity = activity ?? throw new ArgumentNullException(nameof(activity));
		this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public bool IsRunning => Volatile.Read(ref server) is not null;

	public Task StartAsync() => StartAsync(CancellationToken.None);

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ObjectDisposedException.ThrowIf(disposed, this);
			if (server is not null)
				return;

			endpointReporter.Starting();
			var startedAt = DateTimeOffset.UtcNow;
			IServer? candidate = null;
			var startupRan = false;
			try
			{
				ValidateHttpOptions(options.Value.Http);
				await startupService.StartAsync(cancellationToken).ConfigureAwait(false);
				startupRan = true;
				candidate = serverFactory.Create();
				await candidate.StartAsync(application, cancellationToken).ConfigureAwait(false);

				var addresses = candidate.Features.Get<IServerAddressesFeature>()?.Addresses.ToArray() ?? [];
				endpointReporter.Running(addresses);
				activity.Publish(new McpActivityEvent
				{
					Source = "server",
					Kind = "server.start",
					Name = "mcp-http",
					Status = "success",
					Duration = DateTimeOffset.UtcNow - startedAt,
					Summary = endpointReporter.Current.StreamableHttpUrl,
				});
				server = candidate;
				candidate = null;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				if (candidate is not null)
					await DisposeFailedServerAsync(candidate).ConfigureAwait(false);
				if (startupRan)
				{
					await CleanTransportSessionsAfterFailureAsync().ConfigureAwait(false);
					await StopStartupAfterFailureAsync().ConfigureAwait(false);
				}

				logger.LogError(ex, "MCP HTTP server failed to start.");
				ReportStartupFailure(ex);
				throw;
			}
		}
		finally
		{
			lifecycle.Release();
		}
	}

	public Task StopAsync() => StopAsync(CancellationToken.None);

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var current = server;
			server = null;
			if (current is null)
				return;

			Exception? failure = null;
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(5));
			try
			{
				await current.StopAsync(timeout.Token).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				failure = ex;
			}
			finally
			{
				current.Dispose();
				try
				{
					await sessionCleaner.DisposeAllSessionsAsync().ConfigureAwait(false);
				}
				catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
				{
					failure ??= ex;
				}

				try
				{
					await startupService.StopAsync(CancellationToken.None).ConfigureAwait(false);
				}
				catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
				{
					failure ??= ex;
				}
			}

			endpointReporter.Stopped();
			activity.Publish(new McpActivityEvent
			{
				Source = "server",
				Kind = "server.stop",
				Name = "mcp-http",
				Status = failure is null ? "success" : "failure",
				Summary = failure?.Message,
			});

			if (failure is not null)
				ExceptionDispatchInfo.Capture(failure).Throw();
		}
		finally
		{
			lifecycle.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (disposed)
			return;

		await StopAsync().ConfigureAwait(false);
		disposed = true;
		lifecycle.Dispose();
	}

	private async Task DisposeFailedServerAsync(IServer failedServer)
	{
		try
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			await failedServer.StopAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			logger.LogDebug(ex, "Failed MCP HTTP server cleanup encountered an error.");
		}
		finally
		{
			failedServer.Dispose();
		}
	}

	private async Task StopStartupAfterFailureAsync()
	{
		try
		{
			await startupService.StopAsync(CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			logger.LogDebug(ex, "MCP target cleanup after server startup failure encountered an error.");
		}
	}

	private async Task CleanTransportSessionsAfterFailureAsync()
	{
		try
		{
			await sessionCleaner.DisposeAllSessionsAsync().ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			logger.LogDebug(ex, "MCP transport cleanup after server startup failure encountered an error.");
		}
	}

	private void ReportStartupFailure(Exception startupException)
	{
		try
		{
			endpointReporter.Failed(startupException);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			logger.LogWarning(ex, "MCP endpoint failure reporting encountered an error.");
		}

		try
		{
			activity.Publish(new McpActivityEvent
			{
				Source = "server",
				Kind = "server.start",
				Name = "mcp-http",
				Status = "failure",
				Summary = startupException.Message,
			});
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			logger.LogWarning(ex, "MCP startup failure activity reporting encountered an error.");
		}
	}

	private static void ValidateHttpOptions(McpHttpOptions http)
	{
		if (!IsAllowedBindHost(http.Host))
			throw new InvalidOperationException($"HTTP host '{http.Host}' is not allowed. Bind to localhost or a loopback address.");

		if (http.Port < 0 || http.Port > 65_535)
			throw new InvalidOperationException("HTTP port must be between 0 and 65535.");
	}

	private static bool IsAllowedBindHost(string host) =>
		string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
}

internal sealed class McpHttpHostedService : IHostedService
{
	private readonly DeepFlowMcpHost host;

	public McpHttpHostedService(DeepFlowMcpHost host)
	{
		this.host = host ?? throw new ArgumentNullException(nameof(host));
	}

	public Task StartAsync(CancellationToken cancellationToken) => host.StartAsync(cancellationToken);

	public Task StopAsync(CancellationToken cancellationToken) => host.StopAsync(cancellationToken);
}
