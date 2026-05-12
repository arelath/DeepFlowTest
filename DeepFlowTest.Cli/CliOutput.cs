namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public static class CliOutput
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DictionaryKeyPolicy = null,
		WriteIndented = false,
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
			default:
				writer.WriteLine(ToJson(envelope, pretty: true, hideEmpty: true));
				break;
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

	private static void PruneEmpty(JsonNode? node)
	{
		if (node is JsonObject obj)
		{
			var removals = new List<string>();
			foreach (var property in obj.ToArray())
			{
				PruneEmpty(property.Value);
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
