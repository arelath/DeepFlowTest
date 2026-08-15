namespace DeepFlowTest.Mcp.Hosting;

using System;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal interface IMcpHttpServerFactory
{
	IServer Create();
}

internal sealed class KestrelMcpHttpServerFactory : IMcpHttpServerFactory
{
	private readonly IServiceProvider services;
	private readonly IOptions<McpServerOptions> options;
	private readonly ILoggerFactory loggerFactory;

	public KestrelMcpHttpServerFactory(
		IServiceProvider services,
		IOptions<McpServerOptions> options,
		ILoggerFactory loggerFactory)
	{
		this.services = services ?? throw new ArgumentNullException(nameof(services));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
	}

	public IServer Create()
	{
		var serverOptions = new KestrelServerOptions
		{
			ApplicationServices = services,
		};
		var transport = new SocketTransportFactory(
			Options.Create(new SocketTransportOptions()),
			loggerFactory);
		var server = new KestrelServer(Options.Create(serverOptions), transport, loggerFactory);
		var addresses = server.Features.Get<IServerAddressesFeature>()
			?? throw new InvalidOperationException("Kestrel did not expose its address feature.");
		addresses.Addresses.Add(BuildAddress(options.Value.Http));
		return server;
	}

	private static string BuildAddress(McpHttpOptions http)
	{
		var host = string.Equals(http.Host, "::1", StringComparison.Ordinal) ? "[::1]" : http.Host;
		return $"http://{host}:{http.Port}";
	}
}
