namespace DeepFlowTest.Mcp;

using System;
using System.Threading.Tasks;
using System.Windows;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public partial class App : Application
{
	private IHost? host;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		host = McpApplicationHost.Build(e.Args);
		var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Configuration.McpServerOptions>>().Value;
		var window = host.Services.GetRequiredService<MainWindow>();
		MainWindow = window;
		if (options.Http.StartMinimized)
			window.WindowState = WindowState.Minimized;

		window.Show();

		try
		{
			await host.StartAsync();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			host.Services.GetRequiredService<MainWindowViewModel>().LastError = ex.Message;
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (host is not null)
		{
			host.StopAsync().GetAwaiter().GetResult();
			if (host is IAsyncDisposable asyncDisposable)
				asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
			else
				host.Dispose();
		}

		base.OnExit(e);
	}
}
