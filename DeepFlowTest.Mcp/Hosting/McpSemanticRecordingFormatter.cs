namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal sealed class McpSemanticRecordingFormatter : IDisposable
{
	public const string FormatName = "condensed-agent";
	private const string Header = "dft-condensed/1 profile=agent source=compact-json";

	private static readonly SemanticRecordingFormattingOptions FormattingOptions = new()
	{
		PruneStructuralLayoutNodes = true,
	};

	private static readonly IReadOnlyList<string> ExtraSemanticProperties =
	[
		KnownProperties.AutomationIdAlias,
		KnownProperties.AutomationNameAlias,
		KnownProperties.Header,
		KnownProperties.Title,
		KnownProperties.Uid,
		KnownProperties.Checked,
		KnownProperties.IsChecked,
		KnownProperties.IsExpanded,
		KnownProperties.IsFocused,
		KnownProperties.IsKeyboardFocused,
		KnownProperties.IsKeyboardFocusWithin,
		KnownProperties.IsOpen,
		KnownProperties.IsSelected,
		KnownProperties.IsSubmenuOpen,
		KnownProperties.Visibility,
	];

	private readonly StringWriter text = new(CultureInfo.InvariantCulture);
	private readonly ISemanticRecordingFrameWriter writer;
	private int readOffset;
	private bool disposed;

	public McpSemanticRecordingFormatter()
	{
		writer = SemanticRecordingFrameWriter.Create(text, SemanticRecordingOutputFormat.CondensedAgent, FormattingOptions);
	}

	public static IReadOnlyList<string> MergeSemanticProperties(IEnumerable<string> propertyNames)
	{
		List<string> output = [];
		foreach (var propertyName in propertyNames.Concat(ExtraSemanticProperties))
			if (!string.IsNullOrWhiteSpace(propertyName) && !output.Contains(propertyName, StringComparer.Ordinal))
				output.Add(propertyName);

		return output;
	}

	public static McpCondensedRecordingOutput FormatSnapshot(VisualTreeSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		using var formatter = new McpSemanticRecordingFormatter();
		var frame = new SemanticRecordingFrame
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			TimestampUtc = DateTimeOffset.UtcNow,
			Snapshot = snapshot,
		};
		formatter.writer.WriteFrame(frame);
		return new McpCondensedRecordingOutput
		{
			Text = formatter.text.ToString(),
			FrameCount = 1,
		};
	}

	public static McpCondensedRecordingOutput FormatDelta(VisualTreeSnapshot before, VisualTreeSnapshot after)
	{
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(after);

		using var formatter = new McpSemanticRecordingFormatter();
		formatter.writer.WriteFrame(new SemanticRecordingFrame
		{
			FrameKind = "snapshot",
			SequenceNumber = 0,
			TimestampUtc = DateTimeOffset.UtcNow,
			Snapshot = before,
		});
		formatter.text.GetStringBuilder().Clear();
		formatter.text.WriteLine(Header);
		formatter.writer.WriteFrame(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 1,
			TimestampUtc = DateTimeOffset.UtcNow,
			Delta = VisualTreeSnapshotDelta.Create(before, after),
		});
		return new McpCondensedRecordingOutput
		{
			Text = formatter.text.ToString(),
			FrameCount = 1,
		};
	}

	public McpCondensedRecordingOutput FormatStreamMessages(IReadOnlyList<StreamMessage> messages)
	{
		ArgumentNullException.ThrowIfNull(messages);

		var batchCount = 0;
		var frameCount = 0;
		foreach (var message in messages)
		{
			if (!string.Equals(message.StreamKind, ProtocolConstants.StreamKinds.SemanticRecording, StringComparison.Ordinal)
				|| message.Data is null)
			{
				continue;
			}

			var batch = MessagePacker.ConvertTo<SemanticRecordingBatch>(message.Data);
			batchCount++;
			writer.WriteDroppedActionCount(batch.DroppedActionCount);
			foreach (var frame in batch.Frames ?? [])
			{
				writer.WriteFrame(frame);
				frameCount++;
			}
		}

		var builder = text.GetStringBuilder();
		var output = builder.ToString(readOffset, builder.Length - readOffset);
		readOffset = builder.Length;
		return new McpCondensedRecordingOutput
		{
			Text = output,
			BatchCount = batchCount,
			FrameCount = frameCount,
		};
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		writer.Dispose();
		text.Dispose();
	}
}

internal sealed record class McpCondensedRecordingOutput
{
	public string Format { get; init; } = McpSemanticRecordingFormatter.FormatName;

	public bool SemanticPruning { get; init; } = true;

	public string Text { get; init; } = string.Empty;

	public int BatchCount { get; init; }

	public int FrameCount { get; init; }
}
