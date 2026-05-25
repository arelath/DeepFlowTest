namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Threading.Tasks;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

internal sealed class LocalMcpHttpSecurityMiddleware
{
	private readonly RequestDelegate next;
	private readonly IOptions<McpServerOptions> options;

	public LocalMcpHttpSecurityMiddleware(RequestDelegate next, IOptions<McpServerOptions> options)
	{
		this.next = next ?? throw new ArgumentNullException(nameof(next));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
	}

	public async Task InvokeAsync(HttpContext context)
	{
		if (!IsAllowedHost(context.Request.Host.Host))
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			await context.Response.WriteAsync("Host is not allowed.");
			return;
		}

		if (context.Request.Headers.TryGetValue("Origin", out var originValues))
		{
			foreach (var originValue in originValues)
			{
				if (!IsAllowedOrigin(originValue))
				{
					context.Response.StatusCode = StatusCodes.Status403Forbidden;
					await context.Response.WriteAsync("Origin is not allowed.");
					return;
				}
			}
		}

		await next(context);
	}

	private bool IsAllowedHost(string? host)
	{
		if (string.IsNullOrWhiteSpace(host))
			return false;

		if (string.Equals(host, options.Value.Http.Host, StringComparison.OrdinalIgnoreCase))
			return true;

		return IsLoopbackHost(host);
	}

	private static bool IsAllowedOrigin(string? originValue)
	{
		if (string.IsNullOrWhiteSpace(originValue))
			return true;

		return Uri.TryCreate(originValue, UriKind.Absolute, out var origin)
			&& IsLoopbackHost(origin.Host);
	}

	private static bool IsLoopbackHost(string host) =>
		string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
}
