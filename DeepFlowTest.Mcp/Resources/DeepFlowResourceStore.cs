namespace DeepFlowTest.Mcp.Resources;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.Extensions.Options;

internal sealed class DeepFlowResourceStore
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
	};

	private readonly object gate = new();
	private readonly IOptions<McpServerOptions> options;
	private readonly IMcpActivitySink? activity;
	private readonly Dictionary<string, ResourceEntry> entries = [];
	private readonly Queue<ResourceLogEntry> logs = [];
	private long sequence;

	public DeepFlowResourceStore(IOptions<McpServerOptions> options)
		: this(options, activity: null)
	{
	}

	public DeepFlowResourceStore(IOptions<McpServerOptions> options, IMcpActivitySink? activity)
	{
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.activity = activity;
	}

	public DeepFlowResourceReference StoreJson(string uri, object? data, string mimeType = "application/json") =>
		StoreText(uri, JsonSerializer.Serialize(data, JsonOptions), mimeType);

	public DeepFlowResourceReference StoreScreenshot(ScreenshotCommandResponse response)
	{
		ArgumentNullException.ThrowIfNull(response);
		return StoreJson(DeepFlowResourceNames.LatestScreenshot, new
		{
			response.TargetId,
			Format = response.Format.ToProtocolString(),
			response.Width,
			response.Height,
			response.ByteCount,
			response.BytesBase64,
		});
	}

	public DeepFlowResourceReference StoreText(string uri, string text, string mimeType)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(uri);
		ArgumentNullException.ThrowIfNull(text);
		ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

		lock (gate)
		{
			var reference = new DeepFlowResourceReference(uri, mimeType, DateTimeOffset.UtcNow);
			entries[uri] = new ResourceEntry(reference, text);
			activity?.Publish(new McpActivityEvent
			{
				Source = "server",
				Kind = "resource.store",
				Name = uri,
				Status = "success",
				Summary = mimeType,
				Details = new { uri, mimeType, byteCount = text.Length },
			});
			return reference;
		}
	}

	public string ReadText(string uri)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(uri);

		lock (gate)
		{
			return entries.TryGetValue(uri, out var entry)
				? entry.Text
				: JsonSerializer.Serialize(new { available = false, uri, message = "No payload has been captured for this resource yet." }, JsonOptions);
		}
	}

	public void AddLog(string level, string code, string message)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(level);
		ArgumentException.ThrowIfNullOrWhiteSpace(code);
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		lock (gate)
		{
			logs.Enqueue(new ResourceLogEntry(++sequence, DateTimeOffset.UtcNow, level, code, message));
			while (logs.Count > Math.Max(1, options.Value.ResourceRetentionLimit))
				logs.Dequeue();

			entries[DeepFlowResourceNames.RecentLogs] = new ResourceEntry(
				new DeepFlowResourceReference(DeepFlowResourceNames.RecentLogs, "application/json", DateTimeOffset.UtcNow),
				JsonSerializer.Serialize(logs.ToArray(), JsonOptions));
		}
	}

	public IReadOnlyList<string> ListKnownToolNames() =>
	[
		"deepflow_list_processes",
		"deepflow_attach_target",
		"deepflow_launch_target",
		"deepflow_detach_target",
		"deepflow_target_status",
		"deepflow_ping_target",
		"deepflow_get_visual_tree",
		"deepflow_find_elements",
		"deepflow_get_node",
		"deepflow_get_properties",
		"deepflow_suggest_selectors",
		"deepflow_wait_for_element",
		"deepflow_get_binding_failures",
		"deepflow_click_element",
		"deepflow_focus_element",
		"deepflow_type_text",
		"deepflow_press_keys",
		"deepflow_set_property",
		"deepflow_invoke_operation",
		"deepflow_raise_event",
		"deepflow_capture_screenshot",
		"deepflow_configure_diagnostics",
		"deepflow_start_stream",
		"deepflow_read_stream",
		"deepflow_stop_stream",
	];

	private sealed record ResourceEntry(DeepFlowResourceReference Reference, string Text);

	private sealed record ResourceLogEntry(long Sequence, DateTimeOffset TimestampUtc, string Level, string Code, string Message);
}

internal sealed record class DeepFlowResourceReference(string Uri, string MimeType, DateTimeOffset CapturedAtUtc);
