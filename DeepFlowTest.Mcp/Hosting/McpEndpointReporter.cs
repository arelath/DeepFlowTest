namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.Extensions.Options;

internal sealed class McpEndpointReporter
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
	};

	private readonly object gate = new();
	private readonly IOptions<McpServerOptions> options;
	private readonly IMcpActivitySink activity;
	private McpEndpointInfo current = new();

	public McpEndpointReporter(IOptions<McpServerOptions> options, IMcpActivitySink activity)
	{
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.activity = activity ?? throw new ArgumentNullException(nameof(activity));
	}

	public event EventHandler<McpEndpointInfo>? Changed;

	public McpEndpointInfo Current
	{
		get
		{
			lock (gate)
				return current;
		}
	}

	public void Starting() => Update(new McpEndpointInfo { State = "starting" });

	public void Stopped() => Update(new McpEndpointInfo { State = "stopped" });

	public void Failed(Exception exception) =>
		Update(new McpEndpointInfo { State = "failed", Error = exception.Message });

	public void Running(IEnumerable<string> serverAddresses)
	{
		var baseUrl = ResolveBaseUrl(serverAddresses);
		var streamableUrl = CombinePath(baseUrl, options.Value.Http.Path);
		var legacyUrl = options.Value.Http.EnableLegacySse ? CombinePath(baseUrl, "sse") : null;
		var info = new McpEndpointInfo
		{
			State = "running",
			StreamableHttpUrl = streamableUrl,
			LegacySseUrl = legacyUrl,
		};
		Update(info);
		WriteEndpointFile(info);
	}

	private void Update(McpEndpointInfo info)
	{
		info = info with { UpdatedAtUtc = DateTimeOffset.UtcNow };
		lock (gate)
			current = info;

		Changed?.Invoke(this, info);
	}

	private void WriteEndpointFile(McpEndpointInfo info)
	{
		var endpointFile = options.Value.Http.EndpointFile;
		if (string.IsNullOrWhiteSpace(endpointFile) || info.StreamableHttpUrl is null)
			return;

		try
		{
			var directory = Path.GetDirectoryName(endpointFile);
			if (!string.IsNullOrWhiteSpace(directory))
				Directory.CreateDirectory(directory);

			var payload = new
			{
				streamableHttpUrl = info.StreamableHttpUrl,
				legacySseUrl = info.LegacySseUrl,
				processId = Environment.ProcessId,
				startedAtUtc = info.UpdatedAtUtc,
			};
			File.WriteAllText(endpointFile, JsonSerializer.Serialize(payload, JsonOptions));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			activity.Publish(new McpActivityEvent
			{
				Source = "server",
				Kind = "server.endpoint",
				Name = "endpoint-file",
				Status = "failure",
				Summary = ex.Message,
				Details = new { endpointFile },
			});
		}
	}

	private static string ResolveBaseUrl(IEnumerable<string> serverAddresses)
	{
		var address = serverAddresses.FirstOrDefault(IsHttpAddress) ?? $"http://127.0.0.1:4153";
		return address.TrimEnd('/');
	}

	private static bool IsHttpAddress(string address) =>
		address.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
		|| address.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

	private static string CombinePath(string baseAddress, string path)
	{
		path = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
		if (path.Length == 0)
			return baseAddress.TrimEnd('/');

		return baseAddress.TrimEnd('/') + "/" + path.TrimStart('/');
	}
}
