namespace DeepFlowTest.Mcp;

using System.Reflection;
using System.Threading.Tasks;
using DeepFlowTest.Cli;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

internal static class Program
{
	public static Task Main(string[] args)
	{
		var builder = Host.CreateApplicationBuilder(args);
		builder.Logging.ClearProviders();
		builder.Logging.AddConsole(static options =>
		{
			options.LogToStandardErrorThreshold = LogLevel.Trace;
		});

		builder.Services.Configure<DeepFlowMcpOptions>(options =>
		{
			McpCommandLineOptions.Apply(options, args);
		});
		builder.Services.AddSingleton<CliServices>();
		builder.Services.AddSingleton<IMcpProcessLauncher, McpProcessLauncher>();
		builder.Services.AddSingleton<McpTargetSessionFactory>();
		builder.Services.AddSingleton<McpSnapshotCache>();
		builder.Services.AddSingleton<McpStreamRegistry>();
		builder.Services.AddSingleton<DeepFlowResourceStore>();
		builder.Services.AddSingleton<McpSessionHost>();
		builder.Services.AddSingleton<McpToolRunner>();
		builder.Services.AddHostedService<McpStartupService>();

		builder.Services.AddMcpServer()
			.WithStdioServerTransport()
			.WithToolsFromAssembly(Assembly.GetExecutingAssembly())
			.WithPromptsFromAssembly(Assembly.GetExecutingAssembly())
			.WithResourcesFromAssembly(Assembly.GetExecutingAssembly());

		return builder.Build().RunAsync();
	}
}
