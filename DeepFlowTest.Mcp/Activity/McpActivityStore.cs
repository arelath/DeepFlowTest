namespace DeepFlowTest.Mcp.Activity;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.Extensions.Options;

internal sealed class McpActivityStore : IMcpActivitySink
{
	private readonly object gate = new();
	private readonly IOptions<McpServerOptions> options;
	private readonly Queue<McpActivityEvent> events = [];
	private long sequence;

	public McpActivityStore(IOptions<McpServerOptions> options)
	{
		this.options = options ?? throw new ArgumentNullException(nameof(options));
	}

	public event EventHandler<McpActivityEvent>? ActivityPublished;

	public void Publish(McpActivityEvent activity)
	{
		ArgumentNullException.ThrowIfNull(activity);

		McpActivityEvent stored;
		lock (gate)
		{
			stored = activity with
			{
				Sequence = ++sequence,
				TimestampUtc = activity.TimestampUtc == default ? DateTimeOffset.UtcNow : activity.TimestampUtc,
				Details = Redact(activity.Details),
			};
			events.Enqueue(stored);
			while (events.Count > Math.Max(1, options.Value.ActivityRetentionLimit))
				events.Dequeue();
		}

		ActivityPublished?.Invoke(this, stored);
	}

	public IReadOnlyList<McpActivityEvent> Snapshot()
	{
		lock (gate)
			return events.ToArray();
	}

	private static object? Redact(object? details) => details;
}
