namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Diagnostics;
using System.Reflection;
using DeepFlowTest.Automation;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Resources;
using DeepFlowTest.Mcp.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using McpConfigurationOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

internal static class McpServiceCollectionExtensions
{
	public static IServiceCollection AddDeepFlowMcpCore(this IServiceCollection services, McpConfigurationOptions options)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(options);

		services.AddSingleton<IOptions<McpConfigurationOptions>>(Options.Create(options));
		services.AddSingleton<AutomationServices>();
		services.AddSingleton<IMcpProcessLauncher, McpProcessLauncher>();
		services.AddSingleton<McpTargetSessionFactory>();
		services.AddSingleton<McpSnapshotCache>();
		services.AddSingleton<McpElementHandleRegistry>();
		services.AddSingleton<McpStreamRegistry>();
		services.AddSingleton<McpContextRegistry>();
		services.AddSingleton<DeepFlowResourceStore>();
		services.AddSingleton<McpSessionHost>();
		services.AddSingleton<McpToolRunner>();
		services.AddSingleton<McpStartupService>();
		services.AddSingleton<McpActivityStore>();
		services.AddSingleton<IMcpActivitySink>(static services => services.GetRequiredService<McpActivityStore>());
		services.AddSingleton<McpEndpointReporter>();
		services.AddSingleton<DiagnosticListener>(_ => new DiagnosticListener("Microsoft.AspNetCore"));
		services.AddSingleton<DiagnosticSource>(static services => services.GetRequiredService<DiagnosticListener>());
		services.AddSingleton<IHttpContextFactory, DefaultHttpContextFactory>();
		services.AddRouting();

		var toolTypes = options.ToolProfile == McpToolProfile.Full
			? new[] { typeof(AgentTools), typeof(TargetTools), typeof(InspectTools), typeof(ActionTools), typeof(ScreenshotTools), typeof(DiagnosticsTools), typeof(StreamTools) }
			: new[] { typeof(AgentTools) };
		var mcpBuilder = services.AddMcpServer()
			.WithHttpTransport(transport =>
			{
				transport.Stateless = !options.Http.EnableLegacySse;
				if (options.Http.EnableLegacySse)
				{
#pragma warning disable MCP9004
					transport.EnableLegacySse = true;
#pragma warning restore MCP9004
				}
			})
			.WithTools(toolTypes: toolTypes)
			.WithPromptsFromAssembly(Assembly.GetExecutingAssembly())
			.WithResources<AgentResources>();
		if (options.ToolProfile == McpToolProfile.Full)
			mcpBuilder.WithResources<DeepFlowResources>();

		services.AddSingleton<IMcpTransportSessionCleaner, McpTransportSessionCleaner>();
		services.AddSingleton<McpHttpApplication>();
		services.AddSingleton<IMcpHttpServerFactory, KestrelMcpHttpServerFactory>();
		services.AddSingleton<DeepFlowMcpHost>();
		services.AddSingleton<IHostedService, McpHttpHostedService>();
		return services;
	}
}
