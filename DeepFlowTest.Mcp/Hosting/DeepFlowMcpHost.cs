namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

internal sealed class DeepFlowMcpHost : IAsyncDisposable
{
	private readonly object gate = new();
	private readonly IServiceProvider services;
	private readonly IOptions<DeepFlowMcpOptions> options;
	private readonly McpEndpointReporter endpointReporter;
	private readonly IMcpActivitySink activity;
	private readonly ILogger<DeepFlowMcpHost> logger;
	private WebApplication? app;

	public DeepFlowMcpHost(
		IServiceProvider services,
		IOptions<DeepFlowMcpOptions> options,
		McpEndpointReporter endpointReporter,
		IMcpActivitySink activity,
		ILogger<DeepFlowMcpHost> logger)
	{
		this.services = services ?? throw new ArgumentNullException(nameof(services));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.endpointReporter = endpointReporter ?? throw new ArgumentNullException(nameof(endpointReporter));
		this.activity = activity ?? throw new ArgumentNullException(nameof(activity));
		this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public bool IsRunning
	{
		get
		{
			lock (gate)
				return app is not null;
		}
	}

	public async Task StartAsync()
	{
		lock (gate)
		{
			if (app is not null)
				return;
		}

		endpointReporter.Starting();
		var startedAt = DateTimeOffset.UtcNow;
		try
		{
			var built = BuildApplication();
			await built.StartAsync();
			lock (gate)
				app = built;

			var addresses = built.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.ToArray()
				?? built.Urls.ToArray();
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
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			logger.LogError(ex, "MCP HTTP server failed to start.");
			endpointReporter.Failed(ex);
			activity.Publish(new McpActivityEvent
			{
				Source = "server",
				Kind = "server.start",
				Name = "mcp-http",
				Status = "failure",
				Summary = ex.Message,
			});
			throw;
		}
	}

	public async Task StopAsync()
	{
		WebApplication? current;
		lock (gate)
		{
			current = app;
			app = null;
		}

		if (current is null)
			return;

		using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
		await current.StopAsync(timeout.Token);
		await current.DisposeAsync();
		endpointReporter.Stopped();
		activity.Publish(new McpActivityEvent
		{
			Source = "server",
			Kind = "server.stop",
			Name = "mcp-http",
			Status = "success",
		});
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
	}

	private WebApplication BuildApplication()
	{
		var http = options.Value.Http;
		if (!IsAllowedBindHost(http.Host))
			throw new InvalidOperationException($"HTTP host '{http.Host}' is not allowed. Bind to localhost or a loopback address.");

		if (http.Port < 0 || http.Port > 65_535)
			throw new InvalidOperationException("HTTP port must be between 0 and 65535.");

		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			ApplicationName = typeof(DeepFlowMcpHost).Assembly.GetName().Name,
		});
		builder.WebHost.UseUrls($"http://{http.Host}:{http.Port}");
		builder.Configuration["AllowedHosts"] = http.AllowedHosts;
		builder.Logging.ClearProviders();
		builder.Logging.AddDebug();
		builder.Services.AddDeepFlowMcpCoreInstances(services);
		var toolTypes = options.Value.ToolProfile == McpToolProfile.Full
			? new[] { typeof(AgentTools), typeof(TargetTools), typeof(InspectTools), typeof(ActionTools), typeof(ScreenshotTools), typeof(DiagnosticsTools), typeof(StreamTools) }
			: new[] { typeof(AgentTools) };
		builder.Services.AddMcpServer()
			.WithHttpTransport(transport =>
			{
				transport.Stateless = !http.EnableLegacySse;
				if (http.EnableLegacySse)
				{
#pragma warning disable MCP9004
					transport.EnableLegacySse = true;
#pragma warning restore MCP9004
				}
			})
			.WithTools(toolTypes: toolTypes)
			.WithPromptsFromAssembly(Assembly.GetExecutingAssembly())
			.WithResourcesFromAssembly(Assembly.GetExecutingAssembly());

		var webApp = builder.Build();
		webApp.UseMiddleware<LocalMcpHttpSecurityMiddleware>();
		webApp.MapMcp(http.Path);
		return webApp;
	}

	private static bool IsAllowedBindHost(string host) =>
		string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
}
