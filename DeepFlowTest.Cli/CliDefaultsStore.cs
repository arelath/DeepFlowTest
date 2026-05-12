namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class CliDefaultsStore
{
	private static readonly IReadOnlyDictionary<string, DefaultValueDefinition> Definitions =
		new Dictionary<string, DefaultValueDefinition>(StringComparer.Ordinal)
		{
			["timeoutMs"] = DefaultValueDefinition.Int(d => d.TimeoutMs, (d, value) => d.TimeoutMs = value),
			["outputFormat"] = DefaultValueDefinition.String(d => d.OutputFormat, (d, value) => d.OutputFormat = value, new[] { "json", "text" }),
			["hideEmpty"] = DefaultValueDefinition.Bool(d => d.HideEmpty, (d, value) => d.HideEmpty = value),
			["useShortIds"] = DefaultValueDefinition.Bool(d => d.UseShortIds, (d, value) => d.UseShortIds = value),
			["afterSnapshot"] = DefaultValueDefinition.String(d => d.AfterSnapshot, (d, value) => d.AfterSnapshot = value, new[] { "none", "target", "tree" }),
			["treeShape"] = DefaultValueDefinition.String(d => d.TreeShape, (d, value) => d.TreeShape = value, new[] { "flat", "tree" }),
			["treeMaxDepth"] = DefaultValueDefinition.Int(d => d.TreeMaxDepth, (d, value) => d.TreeMaxDepth = value),
			["treeLimit"] = DefaultValueDefinition.Int(d => d.TreeLimit, (d, value) => d.TreeLimit = value),
			["propertyNames"] = DefaultValueDefinition.StringList(d => d.PropertyNames, (d, value) => d.PropertyNames = value),
			["findLimit"] = DefaultValueDefinition.Int(d => d.FindLimit, (d, value) => d.FindLimit = value),
			["waitIntervalMs"] = DefaultValueDefinition.Int(d => d.WaitIntervalMs, (d, value) => d.WaitIntervalMs = value),
			["waitMatchCount"] = DefaultValueDefinition.Int(d => d.WaitMatchCount, (d, value) => d.WaitMatchCount = value),
			["streamIntervalMs"] = DefaultValueDefinition.Int(d => d.StreamIntervalMs, (d, value) => d.StreamIntervalMs = value),
			["screenshotFormat"] = DefaultValueDefinition.String(d => d.ScreenshotFormat, (d, value) => d.ScreenshotFormat = value, new[] { "png", "jpg", "jpeg" }),
			["keyDelayMs"] = DefaultValueDefinition.Int(d => d.KeyDelayMs, (d, value) => d.KeyDelayMs = value),
			["ensureForeground"] = DefaultValueDefinition.Bool(d => d.EnsureForeground, (d, value) => d.EnsureForeground = value),
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
		var defaults = new CliDefaults();
		if (!File.Exists(ConfigPath))
			return defaults;

		var root = ReadConfigObject();
		foreach (var (key, definition) in Definitions)
		{
			if (!root.TryGetPropertyValue(key, out var node) || node is null)
				continue;

			definition.Apply(defaults, node, key);
		}

		Validate(defaults);
		return defaults;
	}

	public object Get(string? key)
	{
		var defaults = Load();
		if (string.IsNullOrWhiteSpace(key))
			return ToDictionary(defaults);

		var definition = GetDefinition(key);
		return definition.Get(defaults) ?? new object();
	}

	public void Set(string key, string value)
	{
		var definition = GetDefinition(key);
		var root = ReadConfigObjectOrEmpty();
		root[key] = definition.Parse(value, key);
		Save(root);
		Load();
	}

	public void Clear(string key)
	{
		_ = GetDefinition(key);
		var root = ReadConfigObjectOrEmpty();
		root.Remove(key);
		Save(root);
	}

	public void Reset()
	{
		if (File.Exists(ConfigPath))
			File.Delete(ConfigPath);
	}

	public IReadOnlyDictionary<string, object?> ToDictionary(CliDefaults defaults)
	{
		return Definitions.ToDictionary(
			static pair => pair.Key,
			pair => pair.Value.Get(defaults),
			StringComparer.Ordinal);
	}

	private static void Validate(CliDefaults defaults)
	{
		foreach (var (key, definition) in Definitions)
			definition.Validate(definition.Get(defaults), key);
	}

	private DefaultValueDefinition GetDefinition(string key)
	{
		if (!Definitions.TryGetValue(key, out var definition))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Unknown config key '{key}'.");

		return definition;
	}

	private JsonObject ReadConfigObjectOrEmpty()
	{
		if (!File.Exists(ConfigPath))
			return new JsonObject();

		return ReadConfigObject();
	}

	private JsonObject ReadConfigObject()
	{
		try
		{
			var text = File.ReadAllText(ConfigPath);
			var node = JsonNode.Parse(text);
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

		File.WriteAllText(ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
	}

	private sealed class DefaultValueDefinition
	{
		private readonly Func<CliDefaults, object?> getter;
		private readonly Action<CliDefaults, JsonNode, string> apply;
		private readonly Func<string, string, JsonNode?> parse;
		private readonly Action<object?, string> validate;

		private DefaultValueDefinition(
			Func<CliDefaults, object?> getter,
			Action<CliDefaults, JsonNode, string> apply,
			Func<string, string, JsonNode?> parse,
			Action<object?, string> validate)
		{
			this.getter = getter;
			this.apply = apply;
			this.parse = parse;
			this.validate = validate;
		}

		public object? Get(CliDefaults defaults) => getter(defaults);

		public void Apply(CliDefaults defaults, JsonNode node, string key) => apply(defaults, node, key);

		public JsonNode? Parse(string value, string key) => parse(value, key);

		public void Validate(object? value, string key) => validate(value, key);

		public static DefaultValueDefinition Int(Func<CliDefaults, int> get, Action<CliDefaults, int> set) =>
			new(
				d => get(d),
				(d, node, key) =>
				{
					try
					{
						set(d, node.GetValue<int>());
					}
					catch (InvalidOperationException ex)
					{
						throw new CliException(CliErrorCodes.InvalidConfig, $"Config key '{key}' must be an integer: {ex.Message}");
					}
				},
				(value, key) =>
				{
					if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
						throw new CliException(CliErrorCodes.InvalidArguments, $"Config key '{key}' must be an integer.");

					return JsonValue.Create(parsed);
				},
				(_, _) => { });

		public static DefaultValueDefinition Bool(Func<CliDefaults, bool> get, Action<CliDefaults, bool> set) =>
			new(
				d => get(d),
				(d, node, key) =>
				{
					try
					{
						set(d, node.GetValue<bool>());
					}
					catch (InvalidOperationException ex)
					{
						throw new CliException(CliErrorCodes.InvalidConfig, $"Config key '{key}' must be a boolean: {ex.Message}");
					}
				},
				(value, key) =>
				{
					if (!bool.TryParse(value, out var parsed))
						throw new CliException(CliErrorCodes.InvalidArguments, $"Config key '{key}' must be a boolean.");

					return JsonValue.Create(parsed);
				},
				(_, _) => { });

		public static DefaultValueDefinition String(
			Func<CliDefaults, string> get,
			Action<CliDefaults, string> set,
			IReadOnlyCollection<string>? allowedValues = null) =>
			new(
				d => get(d),
				(d, node, key) =>
				{
					try
					{
						var value = node.GetValue<string>();
						ValidateAllowed(value, key, allowedValues);
						set(d, value);
					}
					catch (InvalidOperationException ex)
					{
						throw new CliException(CliErrorCodes.InvalidConfig, $"Config key '{key}' must be a string: {ex.Message}");
					}
				},
				(value, key) =>
				{
					ValidateAllowed(value, key, allowedValues);
					return JsonValue.Create(value);
				},
				(value, key) => ValidateAllowed((string?)value ?? string.Empty, key, allowedValues));

		public static DefaultValueDefinition StringList(Func<CliDefaults, List<string>> get, Action<CliDefaults, List<string>> set) =>
			new(
				d => get(d),
				(d, node, key) =>
				{
					if (node is not JsonArray array)
						throw new CliException(CliErrorCodes.InvalidConfig, $"Config key '{key}' must be a string array.");

					var values = new List<string>();
					foreach (var item in array)
						values.Add(item?.GetValue<string>() ?? throw new CliException(CliErrorCodes.InvalidConfig, $"Config key '{key}' must contain only strings."));
					set(d, values);
				},
				(value, _) =>
				{
					var array = new JsonArray();
					if (value.TrimStart().StartsWith("[", StringComparison.Ordinal))
					{
						var parsed = JsonNode.Parse(value) as JsonArray
							?? throw new CliException(CliErrorCodes.InvalidArguments, "String-list config values must be JSON arrays or comma-separated strings.");
						foreach (var item in parsed)
							array.Add(item?.GetValue<string>() ?? throw new CliException(CliErrorCodes.InvalidArguments, "String-list config values must contain only strings."));
					}
					else
					{
						foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
							array.Add(item);
					}

					return array;
				},
				(_, _) => { });

		private static void ValidateAllowed(string value, string key, IReadOnlyCollection<string>? allowedValues)
		{
			if (allowedValues is null)
				return;

			if (!allowedValues.Contains(value, StringComparer.Ordinal))
				throw new CliException(CliErrorCodes.InvalidConfig, $"Config key '{key}' has invalid value '{value}'.");
		}
	}
}
