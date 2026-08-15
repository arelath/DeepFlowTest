namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

internal interface IMcpTransportSessionCleaner
{
	Task DisposeAllSessionsAsync();
}

internal sealed class McpTransportSessionCleaner : IMcpTransportSessionCleaner
{
	private const string SessionManagerTypeName = "ModelContextProtocol.AspNetCore.StatefulSessionManager";
	private readonly IServiceProvider services;
	private readonly IOptions<HttpServerTransportOptions> options;

	public McpTransportSessionCleaner(
		IServiceProvider services,
		IOptions<HttpServerTransportOptions> options)
	{
		this.services = services ?? throw new ArgumentNullException(nameof(services));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
	}

	public async Task DisposeAllSessionsAsync()
	{
		if (options.Value.Stateless)
			return;

		var managerType = typeof(HttpServerTransportOptions).Assembly.GetType(SessionManagerTypeName)
			?? throw new InvalidOperationException($"The MCP transport session manager '{SessionManagerTypeName}' is unavailable.");
		var disposeMethod = managerType.GetMethod(
			"DisposeAllSessionsAsync",
			BindingFlags.Public | BindingFlags.Instance,
			binder: null,
			types: Type.EmptyTypes,
			modifiers: null)
			?? throw new InvalidOperationException("The MCP transport does not expose its session cleanup operation.");
		var manager = services.GetService(managerType)
			?? throw new InvalidOperationException("The MCP transport session manager is not registered.");

		try
		{
			var cleanup = disposeMethod.Invoke(manager, parameters: null) as Task
				?? throw new InvalidOperationException("The MCP transport session cleanup operation returned an invalid result.");
			await cleanup.ConfigureAwait(false);
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
			throw;
		}
	}
}
