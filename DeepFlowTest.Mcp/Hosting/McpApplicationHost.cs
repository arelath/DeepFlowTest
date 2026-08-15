namespace DeepFlowTest.Mcp.Hosting;

using System;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal static class McpApplicationHost
{
	public static IHost Build(string[] args)
	{
		ArgumentNullException.ThrowIfNull(args);

		var options = McpCommandLineOptions.Parse(args);
		return Host.CreateDefaultBuilder()
			.ConfigureLogging(logging =>
			{
				logging.ClearProviders();
				logging.AddDebug();
			})
			.ConfigureServices(services =>
			{
				services.AddDeepFlowMcpCore(options);
				services.AddSingleton<McpGuiSettingsStore>();
				services.AddSingleton<MainWindowViewModel>();
				services.AddSingleton<MainWindow>();
			})
			.Build();
	}
}
