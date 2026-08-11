namespace DeepFlowTest.Mcp.Activity;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Tools;
using Microsoft.Extensions.Options;

internal sealed class McpActivityStore : IMcpActivitySink
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly object gate = new();
	private readonly IOptions<McpServerOptions> options;
	private readonly string? activityLogFile;
	private readonly Queue<McpActivityEvent> events = [];
	private long sequence;

	public McpActivityStore(IOptions<McpServerOptions> options)
	{
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		if (!string.IsNullOrWhiteSpace(options.Value.ActivityLogFile))
		{
			activityLogFile = Path.GetFullPath(options.Value.ActivityLogFile);
			var directory = Path.GetDirectoryName(activityLogFile);
			if (!string.IsNullOrWhiteSpace(directory))
				Directory.CreateDirectory(directory);
			File.WriteAllText(activityLogFile, string.Empty);
		}
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

			if (activityLogFile is not null)
				File.AppendAllText(activityLogFile, JsonSerializer.Serialize(stored, JsonOptions) + Environment.NewLine);
		}

		ActivityPublished?.Invoke(this, stored);
	}

	public IReadOnlyList<McpActivityEvent> Snapshot()
	{
		lock (gate)
			return events.ToArray();
	}

	private static object? Redact(object? details) =>
		details is ToolActivityDetails tool
			? tool with { Result = RedactResult(tool.Result) }
			: details;

	private static object? RedactResult(object? result) =>
		result is ScreenshotCaptureData capture
			? new
			{
				Screenshot = new
				{
					capture.Screenshot.TargetId,
					capture.Screenshot.Format,
					capture.Screenshot.Width,
					capture.Screenshot.Height,
					capture.Screenshot.ByteCount,
					BytesBase64 = "[omitted from activity log]",
				},
				capture.Resource,
			}
			: result;
}
