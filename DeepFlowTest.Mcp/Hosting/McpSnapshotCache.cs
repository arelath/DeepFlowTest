namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.Extensions.Options;

internal sealed class McpSnapshotCache
{
	private readonly IOptions<McpServerOptions> options;
	private readonly object gate = new();
	private readonly Dictionary<CacheKey, CacheEntry> cached = [];

	public McpSnapshotCache(IOptions<McpServerOptions> options)
	{
		this.options = options ?? throw new ArgumentNullException(nameof(options));
	}

	public VisualTreeSnapshot GetOrRefresh(
		McpSessionHost host,
		IReadOnlyList<string> properties,
		int maxNodeCount,
		bool includeHidden = true,
		bool refresh = false,
		string? rootTargetId = null)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(properties);

		var session = host.RequireSession();
		var key = new CacheKey(
			session.SessionId,
			string.Join("|", properties.Order(StringComparer.Ordinal)),
			Math.Max(1, maxNodeCount),
			includeHidden,
			rootTargetId ?? string.Empty);
		if (!refresh)
		{
			lock (gate)
			{
				if (cached.TryGetValue(key, out var entry) && !IsExpired(entry))
					return entry.Snapshot;
			}
		}

		var response = session.AppSession.Send<object>(
			new GetVisualTreeCommandRequest
			{
				PropNames = properties,
				AsSnapshot = true,
				IncludeHidden = includeHidden,
				RootTargetId = rootTargetId,
				MaxNodeCount = Math.Max(1, maxNodeCount),
				TimeoutMs = options.Value.DefaultTimeoutMs,
			},
			options.Value.DefaultTimeoutMs);
		var snapshot = new VisualTreeResponseReader().Read(response, properties);
		lock (gate)
			cached[key] = new CacheEntry(snapshot, DateTimeOffset.UtcNow);

		return snapshot;
	}

	public void Invalidate()
	{
		lock (gate)
			cached.Clear();
	}

	private bool IsExpired(CacheEntry entry) =>
		(DateTimeOffset.UtcNow - entry.CapturedAtUtc).TotalMilliseconds > Math.Max(0, options.Value.CacheTtlMs);

	private sealed record CacheKey(
		Guid SessionId,
		string PropertiesKey,
		int MaxNodeCount,
		bool IncludeHidden,
		string RootTargetId);

	private sealed record CacheEntry(VisualTreeSnapshot Snapshot, DateTimeOffset CapturedAtUtc);
}
