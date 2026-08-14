namespace DeepFlowTest;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DeepFlowTest.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

public enum SemanticRecordingOutputFormat
{
	CondensedAgent,
	CompactJson,
	RawJson,
	CondensedDiagnostic,
}

public sealed class SemanticRecordingFormattingOptions
{
	public bool PruneStructuralLayoutNodes { get; set; }
}

public interface ISemanticRecordingFrameWriter : IDisposable
{
	long FramesWritten { get; }

	void WriteFrame(SemanticRecordingFrame frame);

	void WriteDroppedActionCount(int count);
}

public static class SemanticRecordingFrameWriter
{
	public static ISemanticRecordingFrameWriter Create(
		TextWriter writer,
		SemanticRecordingOutputFormat format,
		SemanticRecordingFormattingOptions? options = null)
	{
		_ = writer ?? throw new ArgumentNullException(nameof(writer));
		return format switch
		{
			SemanticRecordingOutputFormat.RawJson => new JsonSemanticRecordingFrameWriter(writer, compact: false, options),
			SemanticRecordingOutputFormat.CompactJson => new JsonSemanticRecordingFrameWriter(writer, compact: true, options),
			SemanticRecordingOutputFormat.CondensedAgent => new CondensedSemanticRecordingFrameWriter(writer, "agent", options),
			SemanticRecordingOutputFormat.CondensedDiagnostic => new CondensedSemanticRecordingFrameWriter(writer, "diagnostic", options),
			_ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported semantic recording output format."),
		};
	}

	internal static bool IsJson(SemanticRecordingOutputFormat format) =>
		format is SemanticRecordingOutputFormat.RawJson or SemanticRecordingOutputFormat.CompactJson;

	internal static string GetDefaultExtension(SemanticRecordingOutputFormat format) =>
		IsJson(format) ? ".json" : ".dft.txt";
}

internal sealed class JsonSemanticRecordingFrameWriter : ISemanticRecordingFrameWriter
{
	private static readonly JsonSerializerSettings JsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		NullValueHandling = NullValueHandling.Ignore,
		TypeNameHandling = TypeNameHandling.None,
	};

	private readonly TextWriter writer;
	private readonly bool compact;
	private readonly SemanticRecordingFormattingOptions? options;
	private readonly CompactSemanticRecordingState? compactState;
	private bool wroteFrame;
	private bool disposed;

	public JsonSemanticRecordingFrameWriter(TextWriter writer, bool compact, SemanticRecordingFormattingOptions? options)
	{
		this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
		this.compact = compact;
		this.options = options;
		compactState = compact ? new CompactSemanticRecordingState() : null;
		writer.WriteLine("[");
		writer.Flush();
	}

	public long FramesWritten { get; private set; }

	public void WriteFrame(SemanticRecordingFrame frame)
	{
		_ = frame ?? throw new ArgumentNullException(nameof(frame));
		var output = compact ? (object)CompactSemanticRecordingFrame.Create(frame, compactState, options) : frame;
		if (wroteFrame)
			writer.WriteLine(",");

		writer.Write(JsonConvert.SerializeObject(output, Formatting.Indented, JsonSettings));
		writer.Flush();
		wroteFrame = true;
		FramesWritten++;
	}

	public void WriteDroppedActionCount(int count)
	{
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		if (wroteFrame)
			writer.WriteLine();
		writer.WriteLine("]");
		writer.Flush();
	}
}

internal sealed class CondensedSemanticRecordingFrameWriter : ISemanticRecordingFrameWriter
{
	private static readonly string[] IdentityProperties =
	[
		"automationId",
		"name",
		"automationName",
		"text",
		"content",
		"header",
		"title",
		"uid",
		"source",
	];

	private static readonly string[] StateProperties =
	[
		"root",
		"visible",
		"enabled",
		"checked",
		"expanded",
		"open",
		"selected",
		"submenuOpen",
		"visibility",
	];

	private static readonly HashSet<string> StatePropertySet = new(StateProperties, StringComparer.Ordinal);

	private readonly TextWriter writer;
	private readonly SemanticRecordingFormattingOptions? options;
	private readonly CompactSemanticRecordingState compactState = new();
	private bool disposed;

	public CondensedSemanticRecordingFrameWriter(TextWriter writer, string profile, SemanticRecordingFormattingOptions? options)
	{
		this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
		this.options = options;
		writer.WriteLine($"dft-condensed/1 profile={profile} source=compact-json");
		writer.Flush();
	}

	public long FramesWritten { get; private set; }

	public void WriteFrame(SemanticRecordingFrame frame)
	{
		_ = frame ?? throw new ArgumentNullException(nameof(frame));
		var compact = CompactSemanticRecordingFrame.Create(frame, compactState, options);
		if (IsEmptyDelta(compact))
		{
			writer.Flush();
			return;
		}

		WriteCompactFrame(compact);
		writer.Flush();
		FramesWritten++;
	}

	public void WriteDroppedActionCount(int count)
	{
		if (count <= 0)
			return;

		writer.WriteLine($"! droppedActions={count.ToString(CultureInfo.InvariantCulture)}");
		writer.Flush();
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		writer.Flush();
	}

	private void WriteCompactFrame(IReadOnlyDictionary<string, object?> frame)
	{
		var kind = GetString(frame, "kind");
		writer.Write("@");
		writer.Write(GetLong(frame, "seq").ToString(CultureInfo.InvariantCulture));
		writer.Write(' ');
		writer.Write(kind == "recording-started" ? "started" : kind);
		WriteField("at", frame.TryGetValue("at", out var at) ? at : null);

		switch (kind)
		{
			case "recording-started":
				WriteRecordingStarted(frame);
				break;
			case "snapshot":
				WriteSnapshot(frame);
				break;
			case "action":
				WriteAction(frame);
				break;
			case "delta":
				WriteDelta(frame);
				break;
			default:
				writer.WriteLine();
				break;
		}
	}

	private static bool IsEmptyDelta(IReadOnlyDictionary<string, object?> frame)
	{
		if (!string.Equals(GetString(frame, "kind"), "delta", StringComparison.Ordinal))
			return false;
		if (!TryGetDictionary(frame, "delta", out var delta))
			return false;

		return GetLong(delta, "addedCount") == 0
			&& GetLong(delta, "changedCount") == 0
			&& GetLong(delta, "removedCount") == 0;
	}

	private void WriteRecordingStarted(IReadOnlyDictionary<string, object?> frame)
	{
		if (TryGetValue(frame, "recordingId", out var recordingId))
			WriteField("recording", recordingId, allowBareString: true);
		if (TryGetDictionary(frame, "metadata", out var metadata))
		{
			if (TryGetValue(metadata, "processId", out var processId))
				WriteField("process", processId, allowBareString: true);
			foreach (var item in metadata)
			{
				if (string.Equals(item.Key, "processId", StringComparison.Ordinal))
					continue;

				WriteField(item.Key, item.Value, allowBareString: true);
			}
		}

		writer.WriteLine();
	}

	private void WriteSnapshot(IReadOnlyDictionary<string, object?> frame)
	{
		if (!TryGetDictionary(frame, "snapshot", out var snapshot))
		{
			writer.WriteLine();
			return;
		}

		WriteField("treeSeq", GetLong(snapshot, "seq"));
		var includedCount = GetLong(snapshot, "includedCount");
		var nodeCount = GetLong(snapshot, "nodeCount");
		writer.Write(" nodes=");
		writer.Write(includedCount.ToString(CultureInfo.InvariantCulture));
		writer.Write("/");
		writer.Write(nodeCount.ToString(CultureInfo.InvariantCulture));
		if (TryGetValue(snapshot, "omittedCount", out var omittedCount))
			WriteField("omitted", omittedCount);
		if (TryGetBool(snapshot, "truncated", out var truncated) && truncated)
			WriteField("truncated", true);
		if (TryGetValue(snapshot, "truncationReason", out var reason))
			WriteField("reason", reason);
		writer.WriteLine();

		foreach (var node in EnumerateDictionaries(snapshot.TryGetValue("nodes", out var nodes) ? nodes : null))
			WriteNode(node, depth: 0, prefix: string.Empty);
	}

	private void WriteAction(IReadOnlyDictionary<string, object?> frame)
	{
		if (!TryGetDictionary(frame, "action", out var action))
		{
			writer.WriteLine();
			return;
		}

		if (TryGetValue(action, "kind", out var actionKind))
			WriteField("kind", actionKind, allowBareString: true);
		writer.WriteLine();

		if (TryGetDictionary(action, "target", out var target))
			WriteActionTarget(target);
		WriteActionInput(action);
		if (TryGetDictionary(action, "target", out var selectorTarget)
			&& TryGetValue(selectorTarget, "selectors", out var selectors))
		{
			foreach (var selector in EnumerateDictionaries(selectors))
				WriteSelector(selector);
		}
	}

	private void WriteActionTarget(IReadOnlyDictionary<string, object?> target)
	{
		writer.Write("> target ");
		writer.Write(GetString(target, "type", "Target"));
		WriteId(target);
		if (TryGetDictionary(target, "props", out var properties))
			WritePropertyTokens(properties, excludeKeys: []);
		if (TryGetValue(target, "summary", out var summary))
			WriteInlineToken("summary", summary);
		writer.WriteLine();
	}

	private void WriteActionInput(IReadOnlyDictionary<string, object?> action)
	{
		var wroteAny = false;
		foreach (var key in new[] { "mouseButton", "clickCount", "text", "keys" })
		{
			if (!TryGetValue(action, key, out var value))
				continue;

			if (!wroteAny)
			{
				writer.Write("> input");
				wroteAny = true;
			}

			WriteInlineToken(key, value, allowBareString: true);
		}

		if (wroteAny)
			writer.WriteLine();
	}

	private void WriteSelector(IReadOnlyDictionary<string, object?> selector)
	{
		writer.Write("> selector");
		if (TryGetValue(selector, "kind", out var kind))
		{
			writer.Write(' ');
			writer.Write(FormatFieldValue(kind, allowBareString: true));
		}

		foreach (var key in new[] { "property", "value", "confidence", "cli" })
			if (TryGetValue(selector, key, out var value))
				WriteInlineToken(key, value, allowBareString: key is "property");
		writer.WriteLine();
	}

	private void WriteDelta(IReadOnlyDictionary<string, object?> frame)
	{
		if (!TryGetDictionary(frame, "delta", out var delta))
		{
			writer.WriteLine();
			return;
		}

		WriteField("baseTree", GetLong(delta, "baseSeq"));
		WriteField("currentTree", GetLong(delta, "currentSeq"));
		WriteField("added", GetLong(delta, "addedCount"));
		WriteField("changed", GetLong(delta, "changedCount"));
		WriteField("removed", GetLong(delta, "removedCount"));
		writer.WriteLine();

		foreach (var node in EnumerateDictionaries(delta.TryGetValue("added", out var added) ? added : null))
			WriteNode(node, depth: 0, prefix: "+ ");
		foreach (var node in EnumerateDictionaries(delta.TryGetValue("changed", out var changed) ? changed : null))
			WriteChangedNode(node);
		if (TryGetValue(delta, "removed", out var removed))
			WriteRemoved(removed, TryGetValue(delta, "removedOmittedCount", out var omittedCount) ? omittedCount : null);
	}

	private void WriteChangedNode(IReadOnlyDictionary<string, object?> node)
	{
		writer.Write("*");
		if (TryGetValue(node, "type", out var type))
		{
			writer.Write(' ');
			writer.Write(FormatFieldValue(type, allowBareString: true));
		}

		WriteId(node);
		WritePropertyTokens(node, excludeKeys: ["id", "type", "changes"]);
		if (TryGetDictionary(node, "changes", out var changes))
			WritePropertyTokens(changes, excludeKeys: []);
		writer.WriteLine();
	}

	private void WriteRemoved(object? removed, object? omittedCount)
	{
		var ids = EnumerateValues(removed)
			.Select(static value => Convert.ToString(value, CultureInfo.InvariantCulture))
			.Where(static value => !string.IsNullOrWhiteSpace(value))
			.Select(static value => FormatId(value!))
			.ToArray();
		if (ids.Length == 0)
			return;

		writer.Write("- ");
		writer.Write(string.Join(", ", ids));
		if (omittedCount is not null)
			WriteInlineToken("removedOmitted", omittedCount);
		writer.WriteLine();
	}

	private void WriteNode(IReadOnlyDictionary<string, object?> node, int depth, string prefix)
	{
		writer.Write(new string(' ', Math.Max(0, depth) * 2));
		writer.Write(prefix);
		writer.Write(GetString(node, "type", "Node"));
		WriteId(node);
		WritePropertyTokens(node, excludeKeys: ["id", "type", "children"]);
		writer.WriteLine();

		if (TryGetValue(node, "children", out var children))
		{
			foreach (var child in EnumerateDictionaries(children))
				WriteNode(child, depth + 1, prefix);
		}
	}

	private void WritePropertyTokens(IReadOnlyDictionary<string, object?> values, IReadOnlyList<string> excludeKeys)
	{
		var excluded = new HashSet<string>(excludeKeys, StringComparer.Ordinal);
		foreach (var key in IdentityProperties)
		{
			if (!excluded.Contains(key) && TryGetValue(values, key, out var value))
				WriteIdentityToken(key, value);
		}

		foreach (var key in StateProperties)
		{
			if (!excluded.Contains(key) && TryGetValue(values, key, out var value))
				WriteStateToken(key, value);
		}

		foreach (var item in values.OrderBy(static item => item.Key, StringComparer.Ordinal))
		{
			if (excluded.Contains(item.Key)
				|| IdentityProperties.Contains(item.Key, StringComparer.Ordinal)
				|| StatePropertySet.Contains(item.Key))
			{
				continue;
			}

			WriteInlineToken(item.Key, item.Value);
		}
	}

	private void WriteIdentityToken(string key, object? value)
	{
		if (value is string text && IsBareToken(text))
		{
			if (key == "automationId")
			{
				writer.Write(" #");
				writer.Write(text);
				return;
			}

			if (key == "name")
			{
				writer.Write(" .");
				writer.Write(text);
				return;
			}
		}

		var outputKey = key == "automationName" ? "autoName" : key;
		WriteInlineToken(outputKey, value);
	}

	private void WriteStateToken(string key, object? value)
	{
		if (value is bool boolValue)
		{
			writer.Write(' ');
			if (!boolValue)
				writer.Write('!');
			writer.Write(key);
			return;
		}

		WriteInlineToken(key, value);
	}

	private void WriteId(IReadOnlyDictionary<string, object?> values)
	{
		if (!TryGetValue(values, "id", out var id))
			return;

		writer.Write(' ');
		writer.Write(FormatId(Convert.ToString(id, CultureInfo.InvariantCulture) ?? string.Empty));
	}

	private static string FormatId(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
			return "[]";

		var lastDash = id.LastIndexOf('-');
		var shortId = lastDash >= 0 && lastDash + 1 < id.Length
			? id.Substring(lastDash + 1)
			: id;
		return "[" + shortId + "]";
	}

	private void WriteField(string key, object? value, bool allowBareString = false)
	{
		writer.Write(' ');
		writer.Write(key);
		writer.Write('=');
		writer.Write(FormatFieldValue(value, allowBareString));
	}

	private void WriteInlineToken(string key, object? value, bool allowBareString = false)
	{
		writer.Write(' ');
		writer.Write(key);
		writer.Write('=');
		writer.Write(FormatFieldValue(value, allowBareString));
	}

	private static string FormatFieldValue(object? value, bool allowBareString = false)
	{
		if (value is null)
			return "null";

		if (value is DateTimeOffset dateTimeOffset)
			return JsonConvert.SerializeObject(dateTimeOffset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
		if (value is DateTime dateTime)
			return JsonConvert.SerializeObject(dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
		if (value is string text)
			return allowBareString && IsBareToken(text)
				? text
				: JsonConvert.SerializeObject(text);
		if (value is bool boolValue)
			return boolValue ? "true" : "false";
		if (value is IFormattable formattable && value is not IEnumerable)
			return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;

		return JsonConvert.SerializeObject(value, Formatting.None);
	}

	private static bool IsBareToken(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return false;

		foreach (var character in value)
		{
			if (!char.IsLetterOrDigit(character)
				&& character is not '_' and not '-' and not '.' and not ':' and not '/')
			{
				return false;
			}
		}

		return true;
	}

	private static bool TryGetDictionary(
		IReadOnlyDictionary<string, object?> values,
		string key,
		out IReadOnlyDictionary<string, object?> dictionary)
	{
		dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
		if (!TryGetValue(values, key, out var rawValue))
			return false;

		return TryAsDictionary(rawValue, out dictionary);
	}

	private static bool TryAsDictionary(object? value, out IReadOnlyDictionary<string, object?> dictionary)
	{
		if (value is IReadOnlyDictionary<string, object?> readOnly)
		{
			dictionary = readOnly;
			return true;
		}

		if (value is IDictionary<string, object?> generic)
		{
			dictionary = new Dictionary<string, object?>(generic, StringComparer.Ordinal);
			return true;
		}

		dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
		return false;
	}

	private static IEnumerable<IReadOnlyDictionary<string, object?>> EnumerateDictionaries(object? value)
	{
		if (value is null || value is string)
			yield break;

		if (TryAsDictionary(value, out var dictionary))
		{
			yield return dictionary;
			yield break;
		}

		if (value is IEnumerable enumerable)
		{
			foreach (var item in enumerable)
			{
				if (TryAsDictionary(item, out var childDictionary))
					yield return childDictionary;
			}
		}
	}

	private static IEnumerable<object?> EnumerateValues(object? value)
	{
		if (value is null)
			yield break;
		if (value is string)
		{
			yield return value;
			yield break;
		}

		if (value is IEnumerable enumerable)
		{
			foreach (var item in enumerable)
				yield return item;
			yield break;
		}

		yield return value;
	}

	private static bool TryGetValue(IReadOnlyDictionary<string, object?> values, string key, out object? value)
	{
		if (values.TryGetValue(key, out value))
			return true;

		value = null;
		return false;
	}

	private static string GetString(IReadOnlyDictionary<string, object?> values, string key, string fallback = "")
	{
		if (!TryGetValue(values, key, out var value) || value is null)
			return fallback;

		return Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback;
	}

	private static long GetLong(IReadOnlyDictionary<string, object?> values, string key)
	{
		if (!TryGetValue(values, key, out var value) || value is null)
			return 0;

		return Convert.ToInt64(value, CultureInfo.InvariantCulture);
	}

	private static bool TryGetBool(IReadOnlyDictionary<string, object?> values, string key, out bool value)
	{
		value = false;
		if (!TryGetValue(values, key, out var rawValue) || rawValue is null)
			return false;
		if (rawValue is bool boolValue)
		{
			value = boolValue;
			return true;
		}

		return bool.TryParse(Convert.ToString(rawValue, CultureInfo.InvariantCulture), out value);
	}
}
