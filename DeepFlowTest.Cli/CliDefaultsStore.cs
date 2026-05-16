namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DeepFlowTest.Contracts;

public sealed class CliDefaultsStore
{
	private static readonly IReadOnlyDictionary<string, string> PathAliases =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["timeoutMs"] = "common.timeoutMs",
			["outputFormat"] = "common.format",
			["hideEmpty"] = "common.hideEmpty",
			["useShortIds"] = "common.useShortIds",
			["afterSnapshot"] = "common.after",
			["treeShape"] = "commands.tree.shape",
			["treeMaxDepth"] = "commands.tree.maxDepth",
			["treeLimit"] = "commands.tree.limit",
			["propertyNames"] = "commands.tree.props",
			["findLimit"] = "commands.find.limit",
			["waitIntervalMs"] = "commands.wait.intervalMs",
			["waitMatchCount"] = "commands.wait.matchCount",
			["streamDurationMs"] = "commands.stream.durationMs",
			["streamIntervalMs"] = "commands.stream.intervalMs",
			["screenshotFormat"] = "commands.screenshot.imageFormat",
			["keyDelayMs"] = "commands.key.delayMs",
			["ensureForeground"] = "commands.key.foreground",
		};

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		Converters =
		{
			new CliImageFormatJsonConverter(),
			new CliTreeShapeJsonConverter(),
			new CliMouseButtonJsonConverter(),
		},
	};

	public CliDefaultsStore(string? configPath = null)
	{
		ConfigPath = string.IsNullOrWhiteSpace(configPath) ? GetDefaultConfigPath() : configPath;
	}

	public string ConfigPath { get; }

	public static string GetDefaultConfigPath() =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			DeepFlowTest.ProductInfo.Name,
			"cli-defaults.json");

	public CliDefaults Load()
	{
		if (!File.Exists(ConfigPath))
			return new CliDefaults();

		try
		{
			var document = ReadDocument();
			var defaults = document.Deserialize<CliDefaults>(JsonOptions)
				?? throw new CliException(CliErrorCodes.InvalidConfig, "CLI defaults config must be a JSON object.");
			ApplyLegacyFlatValues(defaults, document);
			Validate(defaults);
			return defaults;
		}
		catch (CliException)
		{
			throw;
		}
		catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException)
		{
			throw new CliException(CliErrorCodes.InvalidConfig, $"CLI defaults config is invalid: {ex.Message}");
		}
	}

	public object? Get(string? key)
	{
		var document = CreateCurrentDocument();
		if (string.IsNullOrWhiteSpace(key))
			return document;

		var info = ResolvePath(NormalizeKey(key));
		return GetValue(document, info.Parts)?.DeepClone();
	}

	public void Set(string key, string value)
	{
		Set(key, value, json: false);
	}

	public void Set(string key, string value, bool json)
	{
		var info = ResolvePath(NormalizeKey(key));
		RequireEditableLeaf(info);
		var parsed = ParseValue(info, value, json);
		var document = CreateCurrentDocument();
		SetValue(document, info.Parts, parsed?.DeepClone());
		ValidateDocument(document);
		Save(document);
	}

	public void Clear(string key)
	{
		var info = ResolvePath(NormalizeKey(key));
		RequireEditableLeaf(info);
		var document = CreateCurrentDocument();
		SetValue(document, info.Parts, info.BuiltInValue?.DeepClone());
		ValidateDocument(document);
		Save(document);
	}

	public void Reset()
	{
		Save(CreateBuiltInDocument());
	}

	public JsonObject ToDocument(CliDefaults defaults) =>
		JsonSerializer.SerializeToNode(defaults, JsonOptions) as JsonObject
			?? throw new CliException(CliErrorCodes.InvalidConfig, "Could not serialize CLI defaults.");

	public IReadOnlyDictionary<string, object?> ToDictionary(CliDefaults defaults)
	{
		var document = ToDocument(defaults);
		return document.ToDictionary(static pair => pair.Key, static pair => (object?)pair.Value, StringComparer.Ordinal);
	}

	private static void Validate(CliDefaults defaults)
	{
		if (defaults.SchemaVersion != 1)
			throw new CliException(CliErrorCodes.InvalidConfig, "schemaVersion must be 1.");
		if (defaults.Common is null)
			throw new CliException(CliErrorCodes.InvalidConfig, "common must be a JSON object.");
		if (defaults.Commands is null)
			throw new CliException(CliErrorCodes.InvalidConfig, "commands must be a JSON object.");

		RequireOneOf(defaults.Common.Format, "common.format", "json", "text");
		RequireOneOf(defaults.Common.After, "common.after", "none", "target", "tree");
		RequireDefinedEnum(defaults.Commands.Tree.Shape, "commands.tree.shape");
		RequireStringList(defaults.Commands.Tree.Props, "commands.tree.props");
		RequireNullableStringList(defaults.Commands.Tree.TypeNames, "commands.tree.typeNames");
		RequireStringList(defaults.Commands.Find.Include, "commands.find.include");
		RequireStringList(defaults.Commands.Props.Props, "commands.props.props");
		RequireDefinedEnum(defaults.Commands.Screenshot.ImageFormat, "commands.screenshot.imageFormat");
		RequireStringList(defaults.Commands.Stream.Props, "commands.stream.props");
		RequireDefinedEnum(defaults.Commands.Stream.ImageFormat, "commands.stream.imageFormat");
		RequireDefinedEnum(defaults.Commands.Click.Button, "commands.click.button");
	}

	private static void ValidateDocument(JsonObject document)
	{
		CliDefaults defaults;
		try
		{
			defaults = document.Deserialize<CliDefaults>(JsonOptions)
				?? throw new CliException(CliErrorCodes.InvalidConfig, "CLI defaults config must be a JSON object.");
		}
		catch (JsonException ex)
		{
			throw new CliException(CliErrorCodes.InvalidConfig, $"CLI defaults config is invalid: {ex.Message}");
		}

		Validate(defaults);
	}

	private static void RequireOneOf(string? value, string path, params string[] allowedValues)
	{
		if (value is null || !allowedValues.Contains(value, StringComparer.Ordinal))
			throw new CliException(CliErrorCodes.InvalidConfig, $"Config key '{path}' has invalid value '{value}'.");
	}

	private static void RequireDefinedEnum<TEnum>(TEnum value, string path)
		where TEnum : struct, Enum
	{
		if (!Enum.IsDefined(typeof(TEnum), value))
			throw new CliException(CliErrorCodes.InvalidConfig, $"Config key '{path}' has invalid value '{value}'.");
	}

	private static void RequireStringList(IReadOnlyList<string>? values, string path)
	{
		if (values is null)
			throw new CliException(CliErrorCodes.InvalidConfig, $"{path} must be an array.");

		for (var index = 0; index < values.Count; index++)
			if (string.IsNullOrWhiteSpace(values[index]))
				throw new CliException(CliErrorCodes.InvalidConfig, $"{path}[{index}] must be a non-empty string.");
	}

	private static void RequireNullableStringList(IReadOnlyList<string>? values, string path)
	{
		if (values is not null)
			RequireStringList(values, path);
	}

	private JsonObject CreateCurrentDocument() => ToDocument(Load());

	private static JsonObject CreateBuiltInDocument() =>
		JsonSerializer.SerializeToNode(new CliDefaults(), JsonOptions) as JsonObject
			?? throw new CliException(CliErrorCodes.InvalidConfig, "Could not create built-in CLI defaults.");

	private JsonObject ReadDocument()
	{
		try
		{
			var node = JsonNode.Parse(File.ReadAllText(ConfigPath));
			return node as JsonObject
				?? throw new CliException(CliErrorCodes.InvalidConfig, "CLI defaults config must be a JSON object.");
		}
		catch (CliException)
		{
			throw;
		}
		catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException)
		{
			throw new CliException(CliErrorCodes.InvalidConfig, $"CLI defaults config is invalid: {ex.Message}");
		}
	}

	private void Save(JsonObject root)
	{
		var directory = Path.GetDirectoryName(ConfigPath);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);

		File.WriteAllText(ConfigPath, root.ToJsonString(JsonOptions));
	}

	private static void ApplyLegacyFlatValues(CliDefaults defaults, JsonObject root)
	{
		foreach (var (legacyKey, canonicalPath) in PathAliases)
		{
			if (!root.TryGetPropertyValue(legacyKey, out var value) || value is null)
				continue;

			var info = ResolvePath(canonicalPath);
			ApplyValue(defaults, info.Parts, value);
		}
	}

	private static void ApplyValue(CliDefaults defaults, IReadOnlyList<string> parts, JsonNode value)
	{
		var document = JsonSerializer.SerializeToNode(defaults, JsonOptions) as JsonObject
			?? throw new CliException(CliErrorCodes.InvalidConfig, "Could not serialize CLI defaults.");
		SetValue(document, parts, value.DeepClone());
		var updated = document.Deserialize<CliDefaults>(JsonOptions)
			?? throw new CliException(CliErrorCodes.InvalidConfig, "Could not deserialize CLI defaults.");
		defaults.SchemaVersion = updated.SchemaVersion;
		defaults.Common = updated.Common;
		defaults.Commands = updated.Commands;
	}

	private static CliDefaultsPathInfo ResolvePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || path.Contains('[', StringComparison.Ordinal) || path.Contains(']', StringComparison.Ordinal))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Config path '{path}' is not valid.");

		var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0 || string.Join(".", parts) != path)
			throw new CliException(CliErrorCodes.InvalidArguments, $"Config path '{path}' is not valid.");

		var currentType = typeof(CliDefaults);
		PropertyInfo? property = null;
		foreach (var part in parts)
		{
			property = currentType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
				.FirstOrDefault(candidate => string.Equals(JsonOptions.PropertyNamingPolicy!.ConvertName(candidate.Name), part, StringComparison.Ordinal));
			if (property is null || property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
				throw new CliException(CliErrorCodes.InvalidArguments, $"Unknown config key '{path}'.");

			currentType = property.PropertyType;
		}

		var builtInValue = GetValue(CreateBuiltInDocument(), parts)?.DeepClone();
		return new CliDefaultsPathInfo(path, parts, currentType, IsLeafType(currentType), string.Equals(path, "schemaVersion", StringComparison.Ordinal), IsJsonNull(builtInValue), builtInValue);
	}

	private static bool IsLeafType(Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		return type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(List<string>) || type.IsEnum;
	}

	private static void RequireEditableLeaf(CliDefaultsPathInfo info)
	{
		if (info.IsReadOnly)
			throw new CliException(CliErrorCodes.InvalidArguments, $"Config path '{info.Path}' is read-only.");
		if (!info.IsLeaf)
			throw new CliException(CliErrorCodes.InvalidArguments, $"Config path '{info.Path}' is an object. Set or clear a leaf value instead.");
	}

	private static JsonNode? ParseValue(CliDefaultsPathInfo path, string rawValue, bool json)
	{
		JsonNode? value;
		if (json)
		{
			try
			{
				value = JsonNode.Parse(rawValue);
			}
			catch (JsonException ex)
			{
				throw new CliException(CliErrorCodes.InvalidArguments, $"Value is not valid JSON: {ex.Message}");
			}
		}
		else
		{
			value = ParseText(path, rawValue);
		}

		ValidateValueType(path, value);
		return value;
	}

	private static JsonNode? ParseText(CliDefaultsPathInfo path, string rawValue)
	{
		if (string.Equals(rawValue, "null", StringComparison.OrdinalIgnoreCase))
			return null;

		var type = Nullable.GetUnderlyingType(path.ValueType) ?? path.ValueType;
		if (type == typeof(bool))
		{
			if (bool.TryParse(rawValue, out var value))
				return JsonValue.Create(value);
			throw new CliException(CliErrorCodes.InvalidArguments, $"Config key '{path.Path}' must be a boolean.");
		}

		if (type == typeof(int))
		{
			if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
				return JsonValue.Create(value);
			throw new CliException(CliErrorCodes.InvalidArguments, $"Config key '{path.Path}' must be an integer.");
		}

		if (type == typeof(string))
			return JsonValue.Create(rawValue);

		if (type.IsEnum)
			return JsonValue.Create(ParseEnumText(path.ValueType, path.Path, rawValue));

		if (type == typeof(List<string>))
		{
			var array = new JsonArray();
			foreach (var item in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				array.Add(item);
			return array;
		}

		throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported config value type '{path.ValueType.Name}'.");
	}

	private static void ValidateValueType(CliDefaultsPathInfo path, JsonNode? value)
	{
		if (IsJsonNull(value))
		{
			if (path.BuiltInDefaultIsNull)
				return;
			throw new CliException(CliErrorCodes.InvalidArguments, $"Null is not allowed for config key '{path.Path}'.");
		}

		var type = Nullable.GetUnderlyingType(path.ValueType) ?? path.ValueType;
		if (type == typeof(bool) && value is JsonValue boolValue && boolValue.TryGetValue<bool>(out _))
			return;
		if (type == typeof(int) && value is JsonValue intValue && intValue.TryGetValue<int>(out _))
			return;
		if (type == typeof(string) && value is JsonValue stringValue && stringValue.TryGetValue<string>(out _))
			return;
		if (type.IsEnum && value is JsonValue enumValue && enumValue.TryGetValue<string>(out var enumText))
		{
			ParseEnumText(path.ValueType, path.Path, enumText);
			return;
		}
		if (type == typeof(List<string>) && value is JsonArray array)
		{
			for (var index = 0; index < array.Count; index++)
			{
				if (array[index] is not JsonValue item || !item.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
					throw new CliException(CliErrorCodes.InvalidArguments, $"Config key '{path.Path}' list item {index} must be a non-empty string.");
			}

			return;
		}

		throw new CliException(CliErrorCodes.InvalidArguments, $"Invalid value for config key '{path.Path}'.");
	}

	private static string ParseEnumText(Type enumType, string path, string rawValue)
	{
		try
		{
			if (enumType == typeof(ImageFormat))
				return ImageFormatExtensions.ParseProtocolString(rawValue).ToProtocolString();
			if (enumType == typeof(TreeShape))
				return ProtocolValueMapper.FormatTreeShape(ProtocolValueMapper.ParseTreeShape(rawValue));
			if (enumType == typeof(MouseButtonKind))
				return ProtocolValueMapper.FormatMouseButton(ProtocolValueMapper.ParseMouseButton(rawValue));
		}
		catch (FormatException)
		{
			throw new CliException(CliErrorCodes.InvalidConfig, $"Config key '{path}' has invalid value '{rawValue}'.");
		}

		throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported config enum type '{enumType.Name}'.");
	}

	private static JsonNode? GetValue(JsonObject document, IReadOnlyList<string> parts)
	{
		JsonNode? node = document;
		foreach (var part in parts)
		{
			if (node is not JsonObject obj || !obj.TryGetPropertyValue(part, out node))
				return null;
		}

		return node;
	}

	private static void SetValue(JsonObject document, IReadOnlyList<string> parts, JsonNode? value)
	{
		var current = document;
		foreach (var part in parts.Take(parts.Count - 1))
		{
			if (current[part] is not JsonObject child)
			{
				child = new JsonObject();
				current[part] = child;
			}

			current = child;
		}

		current[parts[^1]] = value;
	}

	private static bool IsJsonNull(JsonNode? node) =>
		node is null || node.GetValueKind() == JsonValueKind.Null;

	private static string NormalizeKey(string key) =>
		PathAliases.TryGetValue(key, out var normalized) ? normalized : key;

	private sealed record CliDefaultsPathInfo(
		string Path,
		IReadOnlyList<string> Parts,
		Type ValueType,
		bool IsLeaf,
		bool IsReadOnly,
		bool BuiltInDefaultIsNull,
		JsonNode? BuiltInValue);
}
