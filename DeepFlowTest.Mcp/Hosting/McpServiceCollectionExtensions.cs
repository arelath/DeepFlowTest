namespace DeepFlowTest.Mcp.Hosting;

using System;
using DeepFlowTest.Automation;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

internal static class McpServiceCollectionExtensions
{
	public static IServiceCollection AddDeepFlowMcpCore(this IServiceCollection services, string[] args)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(args);

		services.Configure<McpServerOptions>(options => McpCommandLineOptions.Apply(options, args));
		services.AddSingleton<AutomationServices>();
		services.AddSingleton<IMcpProcessLauncher, McpProcessLauncher>();
		services.AddSingleton<McpTargetSessionFactory>();
		services.AddSingleton<McpSnapshotCache>();
		services.AddSingleton<McpElementHandleRegistry>();
		services.AddSingleton<McpStreamRegistry>();
		services.AddSingleton<DeepFlowResourceStore>();
		services.AddSingleton<McpSessionHost>();
		services.AddSingleton<McpToolRunner>();
		services.AddSingleton<McpStartupService>();
		services.AddSingleton<McpActivityStore>();
		services.AddSingleton<IMcpActivitySink>(static services => services.GetRequiredService<McpActivityStore>());
		services.AddSingleton<McpEndpointReporter>();
		services.AddSingleton<DeepFlowMcpHost>();
		return services;
	}

	public static IServiceCollection AddDeepFlowMcpCoreInstances(this IServiceCollection services, IServiceProvider source)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(source);

		services.AddSingleton(source.GetRequiredService<IOptions<McpServerOptions>>());
		services.AddSingleton(source.GetRequiredService<AutomationServices>());
		services.AddSingleton(source.GetRequiredService<IMcpProcessLauncher>());
		services.AddSingleton(source.GetRequiredService<McpTargetSessionFactory>());
		services.AddSingleton(source.GetRequiredService<McpSnapshotCache>());
		services.AddSingleton(source.GetRequiredService<McpElementHandleRegistry>());
		services.AddSingleton(source.GetRequiredService<McpStreamRegistry>());
		services.AddSingleton(source.GetRequiredService<DeepFlowResourceStore>());
		services.AddSingleton(source.GetRequiredService<McpSessionHost>());
		services.AddSingleton(source.GetRequiredService<McpToolRunner>());
		services.AddSingleton(source.GetRequiredService<McpActivityStore>());
		services.AddSingleton<IMcpActivitySink>(source.GetRequiredService<IMcpActivitySink>());
		services.AddSingleton(source.GetRequiredService<McpEndpointReporter>());
		services.AddSingleton<IHostedService>(source.GetRequiredService<McpStartupService>());
		return services;
	}
}
