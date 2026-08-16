namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Automation;
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
		string? rootTargetId = null,
		int? commandTimeoutMs = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(properties);

		var session = host.RequireSession();
		return GetOrRefresh(session, properties, maxNodeCount, includeHidden, refresh, rootTargetId, commandTimeoutMs, cancellationToken);
	}

	public VisualTreeSnapshot GetOrRefresh(
		McpSession session,
		IReadOnlyList<string> properties,
		int maxNodeCount,
		bool includeHidden = true,
		bool refresh = false,
		string? rootTargetId = null,
		int? commandTimeoutMs = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(properties);

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

		var effectiveTimeout = Math.Max(1, commandTimeoutMs ?? options.Value.DefaultTimeoutMs);
		var response = session.AppSession.Send<object>(
			new GetVisualTreeCommandRequest
			{
				PropNames = properties,
				AsSnapshot = true,
				IncludeHidden = includeHidden,
				RootTargetId = rootTargetId,
				MaxNodeCount = Math.Max(1, maxNodeCount),
				TimeoutMs = effectiveTimeout,
			},
			effectiveTimeout,
			cancellationToken);
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

	public async Task<VisualTreeSnapshot> GetOrRefreshAsync(
		McpSession session,
		IReadOnlyList<string> properties,
		int maxNodeCount,
		bool includeHidden = true,
		bool refresh = false,
		string? rootTargetId = null,
		int? commandTimeoutMs = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(properties);

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

		var effectiveTimeout = Math.Max(1, commandTimeoutMs ?? options.Value.DefaultTimeoutMs);
		var response = await session.AppSession.SendAsync<object>(
			new GetVisualTreeCommandRequest
			{
				PropNames = properties,
				AsSnapshot = true,
				IncludeHidden = includeHidden,
				RootTargetId = rootTargetId,
				MaxNodeCount = Math.Max(1, maxNodeCount),
				TimeoutMs = effectiveTimeout,
			},
			effectiveTimeout,
			cancellationToken).ConfigureAwait(false);
		var snapshot = new VisualTreeResponseReader().Read(response, properties);
		lock (gate)
			cached[key] = new CacheEntry(snapshot, DateTimeOffset.UtcNow);
		return snapshot;
	}

	public void Invalidate(Guid sessionId)
	{
		lock (gate)
		{
			foreach (var key in cached.Keys.Where(key => key.SessionId == sessionId).ToArray())
				cached.Remove(key);
		}
	}

	public long? GetLatestRevision(Guid sessionId)
	{
		lock (gate)
			return cached.Where(pair => pair.Key.SessionId == sessionId).Select(pair => (long?)pair.Value.Snapshot.SequenceNumber).Max();
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
