namespace DeepFlowTest.Mcp;

using System;
using System.Threading.Tasks;
using System.Windows;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public partial class App : Application
{
	private ServiceProvider? services;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		var serviceCollection = new ServiceCollection();
		serviceCollection.AddLogging(builder =>
		{
			builder.ClearProviders();
			builder.AddDebug();
		});
		serviceCollection.AddDeepFlowMcpCore(e.Args);
		serviceCollection.AddSingleton<MainWindowViewModel>();
		serviceCollection.AddSingleton<MainWindow>();

		services = serviceCollection.BuildServiceProvider();
		var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Configuration.McpServerOptions>>().Value;
		var window = services.GetRequiredService<MainWindow>();
		MainWindow = window;
		if (options.Http.StartMinimized)
			window.WindowState = WindowState.Minimized;

		window.Show();

		var host = services.GetRequiredService<DeepFlowMcpHost>();
		try
		{
			await host.StartAsync();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			services.GetRequiredService<MainWindowViewModel>().LastError = ex.Message;
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (services is not null)
		{
			var host = services.GetService<DeepFlowMcpHost>();
			if (host is not null)
				host.StopAsync().GetAwaiter().GetResult();

			services.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}

		base.OnExit(e);
	}
}
