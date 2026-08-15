namespace DeepFlowTest.Mcp.Tests;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DeepFlowTest.Mcp;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using NUnit.Framework;

[TestFixture]
public sealed class McpCompositionRootTests
{
	[Test]
	[CancelAfter(30_000)]
	public async Task RootHostOwnsGuiCoreAndRestartableHttpServer()
	{
		var host = McpApplicationHost.Build(["--http-port", "0", "--http-enable-legacy-sse"]);
		try
		{
			var services = host.Services;
			var registrations = services.GetRequiredService<IServiceProviderIsService>();
			Assert.Multiple(() =>
			{
				Assert.That(registrations.IsService(typeof(MainWindow)), Is.True);
				Assert.That(registrations.IsService(typeof(MainWindowViewModel)), Is.True);
				Assert.That(registrations.IsService(typeof(McpContextRegistry)), Is.True);
				Assert.That(registrations.IsService(typeof(McpActivityStore)), Is.True);
				Assert.That(registrations.IsService(typeof(McpHttpApplication)), Is.True);
			});

			var activityStore = services.GetRequiredService<McpActivityStore>();
			Assert.That(services.GetRequiredService<IMcpActivitySink>(), Is.SameAs(activityStore));
			Assert.That(services.GetServices<IHostedService>().OfType<McpHttpHostedService>().Count(), Is.EqualTo(1));

			var server = services.GetRequiredService<DeepFlowMcpHost>();
			var endpointReporter = services.GetRequiredService<McpEndpointReporter>();
			await host.StartAsync();
			var firstEndpoint = new Uri(endpointReporter.Current.StreamableHttpUrl!);
			services.GetRequiredService<IOptions<McpServerOptions>>().Value.Http.Port = firstEndpoint.Port;
			Assert.Multiple(() =>
			{
				Assert.That(server.IsRunning, Is.True);
				Assert.That(endpointReporter.Current.State, Is.EqualTo("running"));
				Assert.That(endpointReporter.Current.StreamableHttpUrl, Does.StartWith("http://127.0.0.1:"));
			});

			var oldClient = await McpClient.CreateAsync(
				new HttpClientTransport(
					new HttpClientTransportOptions
					{
						Endpoint = firstEndpoint,
						TransportMode = HttpTransportMode.StreamableHttp,
					},
					NullLoggerFactory.Instance),
				clientOptions: null,
				loggerFactory: NullLoggerFactory.Instance,
				cancellationToken: TestContext.CurrentContext.CancellationToken);
			await oldClient.ListToolsAsync(cancellationToken: TestContext.CurrentContext.CancellationToken);

			await server.StopAsync();
			Assert.Multiple(() =>
			{
				Assert.That(server.IsRunning, Is.False);
				Assert.That(endpointReporter.Current.State, Is.EqualTo("stopped"));
			});

			await server.StartAsync();
			Assert.Multiple(() =>
			{
				Assert.That(server.IsRunning, Is.True);
				Assert.That(endpointReporter.Current.State, Is.EqualTo("running"));
				Assert.That(activityStore.Snapshot().Count(activity => activity.Kind == "server.start" && activity.Status == "success"), Is.EqualTo(2));
			});
			Assert.That(
				async () => await oldClient.ListToolsAsync(cancellationToken: TestContext.CurrentContext.CancellationToken),
				Throws.Exception,
				"A stateful session from the stopped listener must not survive restart.");
			await oldClient.DisposeAsync();

			await using var newClient = await McpClient.CreateAsync(
				new HttpClientTransport(
					new HttpClientTransportOptions
					{
						Endpoint = firstEndpoint,
						TransportMode = HttpTransportMode.StreamableHttp,
					},
					NullLoggerFactory.Instance),
				clientOptions: null,
				loggerFactory: NullLoggerFactory.Instance,
				cancellationToken: TestContext.CurrentContext.CancellationToken);
			Assert.That(await newClient.ListToolsAsync(cancellationToken: TestContext.CurrentContext.CancellationToken), Is.Not.Empty);
		}
		finally
		{
			await host.StopAsync();
			if (host is IAsyncDisposable asyncDisposable)
				await asyncDisposable.DisposeAsync();
			else
				host.Dispose();
		}
	}

	[Test]
	[CancelAfter(30_000)]
	public async Task StartupFinalizationFailureReleasesBoundHttpServer()
	{
		var port = ReserveLoopbackPort();
		var activityPath = Path.Combine(Path.GetTempPath(), "DeepFlowTest.Mcp.Tests", Guid.NewGuid().ToString("N"), "activity.jsonl");
		var host = McpApplicationHost.Build([
			"--http-port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
			"--activity-log-file", activityPath,
		]);
		try
		{
			var server = host.Services.GetRequiredService<DeepFlowMcpHost>();
			File.Delete(activityPath);
			Directory.CreateDirectory(activityPath);

			Assert.That(
				async () => await server.StartAsync(TestContext.CurrentContext.CancellationToken),
				Throws.Exception);
			Assert.Multiple(() =>
			{
				Assert.That(server.IsRunning, Is.False);
				Assert.That(host.Services.GetRequiredService<McpEndpointReporter>().Current.State, Is.EqualTo("failed"));
			});

			using var listener = new TcpListener(IPAddress.Loopback, port);
			Assert.DoesNotThrow(listener.Start, "The failed startup must release its bound port.");
		}
		finally
		{
			if (host is IAsyncDisposable asyncDisposable)
				await asyncDisposable.DisposeAsync();
			else
				host.Dispose();

			if (Directory.Exists(activityPath))
				Directory.Delete(activityPath);
			var parent = Path.GetDirectoryName(activityPath);
			if (parent is not null && Directory.Exists(parent))
				Directory.Delete(parent);
		}
	}

	private static int ReserveLoopbackPort()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		return ((IPEndPoint)listener.LocalEndpoint).Port;
	}
}
