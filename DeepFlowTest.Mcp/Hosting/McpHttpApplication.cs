namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Threading.Tasks;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

internal sealed class McpHttpApplication : IHttpApplication<HttpContext>
{
	private readonly IHttpContextFactory contextFactory;
	private readonly RequestDelegate pipeline;

	public McpHttpApplication(
		IServiceProvider services,
		IHttpContextFactory contextFactory,
		IOptions<McpServerOptions> options)
	{
		ArgumentNullException.ThrowIfNull(services);
		this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
		ArgumentNullException.ThrowIfNull(options);

		var app = new ApplicationBuilder(services);
		app.UseRouting();
		app.UseMiddleware<LocalMcpHttpSecurityMiddleware>();
		app.UseEndpoints(endpoints => endpoints.MapMcp(options.Value.Http.Path));
		pipeline = app.Build();
	}

	public HttpContext CreateContext(Microsoft.AspNetCore.Http.Features.IFeatureCollection contextFeatures) =>
		contextFactory.Create(contextFeatures);

	public Task ProcessRequestAsync(HttpContext context) => pipeline(context);

	public void DisposeContext(HttpContext context, Exception? exception) => contextFactory.Dispose(context);
}
