namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public static class CliOutput
{
	private static readonly HashSet<string> RequiredEmptyFields = new(StringComparer.Ordinal)
	{
		"activeSubscriptions",
		"ancestors",
		"children",
		"frames",
		"matches",
		"nodes",
		"processes",
		"requestedProperties",
		"roots",
		"subtree",
		"suggestions",
		"warnings",
	};

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DictionaryKeyPolicy = null,
		WriteIndented = false,
		Converters =
		{
			new CliImageFormatJsonConverter(),
			new CliTreeShapeJsonConverter(),
			new CliMouseButtonJsonConverter(),
			new CliBindingFailureSeverityJsonConverter(),
		},
	};

	public static void Write(CliResponseEnvelope envelope, CliCommonOptions options, TextWriter writer)
	{
		_ = envelope ?? throw new ArgumentNullException(nameof(envelope));
		_ = options ?? throw new ArgumentNullException(nameof(options));
		_ = writer ?? throw new ArgumentNullException(nameof(writer));

		if (string.Equals(options.Format, "text", StringComparison.OrdinalIgnoreCase))
		{
			WriteText(envelope, writer);
			return;
		}

		WriteJson(envelope, options, writer);
	}

	public static void Write(CliResponseSequence sequence, CliCommonOptions options, TextWriter writer)
	{
		_ = sequence ?? throw new ArgumentNullException(nameof(sequence));
		_ = options ?? throw new ArgumentNullException(nameof(options));
		_ = writer ?? throw new ArgumentNullException(nameof(writer));

		if (string.Equals(options.Format, "text", StringComparison.OrdinalIgnoreCase)
			&& IsSemanticRecordingStreamSequence(sequence))
		{
			WriteSemanticRecordingStreamText(sequence, writer);
			return;
		}

		foreach (var envelope in sequence.Envelopes)
			Write(envelope, options, writer);
	}

	public static string ToJson(CliResponseEnvelope envelope, bool pretty = false, bool hideEmpty = true)
	{
		var jsonOptions = new JsonSerializerOptions(JsonOptions) { WriteIndented = pretty };
		var node = JsonSerializer.SerializeToNode(envelope, jsonOptions) ?? new JsonObject();
		if (hideEmpty)
			PruneEmpty(node);

		return node.ToJsonString(jsonOptions);
	}

	private static void WriteJson(CliResponseEnvelope envelope, CliCommonOptions options, TextWriter writer)
	{
		writer.WriteLine(ToJson(envelope, options.Pretty, options.HideEmpty));
	}

	private static void WriteText(CliResponseEnvelope envelope, TextWriter writer)
	{
		if (!envelope.Ok)
		{
			writer.WriteLine($"{envelope.Error?.Code ?? CliErrorCodes.UnexpectedError}: {envelope.Error?.Message}");
			return;
		}

		switch (envelope.Command)
		{
			case "version":
				if (envelope.Data is ProductVersionData version)
					writer.WriteLine(version.ProductName);
				else
					writer.WriteLine(DeepFlowTest.ProductInfo.Name);
				break;
			case "processes":
				WriteProcessesText(envelope.Data, writer);
				break;
			case "ping":
				WritePingText(envelope.Data, writer);
				break;
			case "pipe status":
				WritePipeStatusText(envelope.Data, writer);
				break;
			case "tree":
				WriteTreeText(envelope.Data, writer);
				break;
			case "node":
				WriteNodeText(envelope.Data, writer);
				break;
			case "props":
				WritePropsText(envelope.Data, writer);
				break;
			case "selectors":
				WriteSelectorsText(envelope.Data, writer);
				break;
			default:
				writer.WriteLine(ToJson(envelope, pretty: true, hideEmpty: true));
				break;
		}
	}

	private static bool IsSemanticRecordingStreamSequence(CliResponseSequence sequence) =>
		sequence.Envelopes.Any(static envelope =>
			envelope.Data is StreamMessage { StreamKind: ProtocolConstants.StreamKinds.SemanticRecording }
			|| string.Equals(envelope.Command, "stream semantic-recording frame", StringComparison.Ordinal));

	private static void WriteSemanticRecordingStreamText(CliResponseSequence sequence, TextWriter writer)
	{
		using var recordingWriter = SemanticRecordingFrameWriter.Create(writer, SemanticRecordingOutputFormat.CondensedAgent);
		foreach (var envelope in sequence.Envelopes)
		{
			if (!envelope.Ok)
			{
				writer.WriteLine($"{envelope.Error?.Code ?? CliErrorCodes.UnexpectedError}: {envelope.Error?.Message}");
				continue;
			}

			if (envelope.Data is not StreamMessage { StreamKind: ProtocolConstants.StreamKinds.SemanticRecording } message)
				continue;
			if (message.Error is not null)
			{
				writer.WriteLine($"{message.Error.Code}: {message.Error.Message}");
				continue;
			}

			if (message.Data is null)
				continue;

			var batch = MessagePacker.ConvertTo<SemanticRecordingBatch>(message.Data);
			recordingWriter.WriteDroppedActionCount(batch.DroppedActionCount);
			foreach (var frame in batch.Frames ?? [])
				recordingWriter.WriteFrame(frame);
		}
	}

	private static void WriteProcessesText(object? data, TextWriter writer)
	{
		if (data is not ProcessListData processList)
		{
			writer.WriteLine("No processes.");
			return;
		}

		writer.WriteLine("PID     PROCESS                         WPF   ARCH       FRAMEWORK        WINDOW");
		foreach (var process in processList.Processes)
		{
			var title = process.MainWindowTitle ?? string.Empty;
			if (title.Length > 60)
				title = title[..57] + "...";

			writer.WriteLine(
				$"{process.ProcessId,-7} {Truncate(process.ProcessName, 30),-30} {FormatBool(process.IsLikelyWpfCandidate),-5} {process.Architecture ?? "",-10} {process.FrameworkFamily ?? "",-16} {title}");
		}

		foreach (var warning in processList.Warnings)
			writer.WriteLine($"warning: {warning.Message}");
	}

	private static void WritePingText(object? data, TextWriter writer)
	{
		var ping = UnwrapProtocolData<PingCommandResponse>(data);
		if (ping is null)
		{
			writer.WriteLine(ToJson(CliResponseFactory.Success("ping", data, System.Diagnostics.Stopwatch.StartNew()), pretty: true, hideEmpty: true));
			return;
		}

		writer.WriteLine($"process: {ping.ProcessId}");
		writer.WriteLine($"wpf: {FormatBool(ping.IsWpfAvailable)}");
		writer.WriteLine($"winforms: {FormatBool(ping.IsWinFormsAvailable)}");
		writer.WriteLine($"native: {FormatBool(ping.IsNativeFallbackAvailable)}");
		writer.WriteLine($"dispatcher: {FormatBool(ping.IsDispatcherAvailable)}");
		writer.WriteLine($"roots: {ping.RootCount}");
	}

	private static void WritePipeStatusText(object? data, TextWriter writer)
	{
		var status = UnwrapProtocolData<PipeStatusCommandResponse>(data);
		if (status is null)
		{
			writer.WriteLine(ToJson(CliResponseFactory.Success("pipe status", data, System.Diagnostics.Stopwatch.StartNew()), pretty: true, hideEmpty: true));
			return;
		}

		writer.WriteLine($"pipe: {status.PipeName}");
		writer.WriteLine($"reusable: {FormatBool(status.IsReusable)}");
		writer.WriteLine($"busy: {FormatBool(status.IsBusy)}");
		writer.WriteLine($"sending: {FormatBool(status.IsSending)}");
		writer.WriteLine($"subscriptions: {status.ActiveSubscriptionCount}");
		writer.WriteLine($"commands: {status.TotalCommandsHandled}");
		writer.WriteLine($"idle: {status.IdleMode}");
	}

	private static void WriteTreeText(object? data, TextWriter writer)
	{
		if (data is not TreeSnapshotData tree)
		{
			writer.WriteLine(ToJson(CliResponseFactory.Success("tree", data, System.Diagnostics.Stopwatch.StartNew()), pretty: true, hideEmpty: true));
			return;
		}

		writer.WriteLine($"shape: {tree.Shape}");
		writer.WriteLine($"nodes: {tree.NodeCount}/{tree.TotalNodeCount}");
		if (tree.Truncated)
			writer.WriteLine($"truncated: {tree.TruncationReason ?? "yes"}");
		foreach (var node in tree.Nodes.Count != 0 ? tree.Nodes : FlattenRoots(tree.Roots))
			writer.WriteLine($"{new string(' ', Math.Max(0, node.Depth) * 2)}{node.TargetId} {node.TypeName ?? node.FrameworkTypeName ?? string.Empty}".TrimEnd());
	}

	private static void WriteNodeText(object? data, TextWriter writer)
	{
		if (data is not NodeResultData node)
		{
			writer.WriteLine(ToJson(CliResponseFactory.Success("node", data, System.Diagnostics.Stopwatch.StartNew()), pretty: true, hideEmpty: true));
			return;
		}

		WriteNodeLine(node.Node, writer);
		foreach (var property in node.Node.Properties)
			writer.WriteLine($"{property.Key}: {property.Value}");
	}

	private static void WritePropsText(object? data, TextWriter writer)
	{
		if (data is not PropsResultData props)
		{
			writer.WriteLine(ToJson(CliResponseFactory.Success("props", data, System.Diagnostics.Stopwatch.StartNew()), pretty: true, hideEmpty: true));
			return;
		}

		writer.WriteLine(props.TargetId);
		foreach (var property in props.Properties)
			writer.WriteLine($"{property.Key}: {property.Value}");
	}

	private static void WriteSelectorsText(object? data, TextWriter writer)
	{
		if (data is not SelectorSuggestionData selectors)
		{
			writer.WriteLine(ToJson(CliResponseFactory.Success("selectors", data, System.Diagnostics.Stopwatch.StartNew()), pretty: true, hideEmpty: true));
			return;
		}

		writer.WriteLine(selectors.TargetId);
		foreach (var suggestion in selectors.Suggestions)
			writer.WriteLine($"{suggestion.Confidence:0.00} {suggestion.Cli}");
	}

	private static void PruneEmpty(JsonNode? node)
	{
		if (node is JsonObject obj)
		{
			List<string> removals = [];
			foreach (var property in obj.ToArray())
			{
				PruneEmpty(property.Value);
				if (RequiredEmptyFields.Contains(property.Key))
					continue;

				if (property.Value is null)
				{
					removals.Add(property.Key);
					continue;
				}

				if (property.Value is JsonObject childObject && childObject.Count == 0)
					removals.Add(property.Key);
				else if (property.Value is JsonArray childArray && childArray.Count == 0)
					removals.Add(property.Key);
			}

			foreach (var key in removals)
				obj.Remove(key);
		}
		else if (node is JsonArray array)
		{
			foreach (var child in array)
				PruneEmpty(child);
		}
	}

	private static string FormatBool(bool value) => value ? "yes" : "no";

	private static T? UnwrapProtocolData<T>(object? data)
	{
		if (data is ProtocolCommandData<T> protocol)
			return protocol.Response;

		return data is T typed ? typed : default;
	}

	private static IEnumerable<TreeNodeData> FlattenRoots(IEnumerable<TreeNodeData> roots)
	{
		foreach (var root in roots)
		{
			yield return root;
			foreach (var child in FlattenRoots(root.Children))
				yield return child;
		}
	}

	private static void WriteNodeLine(TreeNodeData node, TextWriter writer)
	{
		var typeName = node.TypeName ?? node.FrameworkTypeName ?? string.Empty;
		writer.WriteLine($"{node.TargetId} {typeName}".TrimEnd());
	}

	private static string Truncate(string value, int length)
	{
		if (value.Length <= length)
			return value;

		return value[..Math.Max(0, length - 3)] + "...";
	}
}

public sealed class ProductVersionData
{
	public string ProductName { get; set; } = string.Empty;
}
