namespace DeepFlowTest.Mcp.Configuration;

using System;
using System.IO;
using System.Text.Json;

internal sealed class McpGuiSettingsStore
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
	};

	public McpGuiSettingsStore(string? configPath = null)
	{
		ConfigPath = string.IsNullOrWhiteSpace(configPath) ? GetDefaultConfigPath() : configPath;
	}

	public string ConfigPath { get; }

	public static string GetDefaultConfigPath() =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			DeepFlowTest.ProductInfo.Name,
			"mcp-gui-settings.json");

	public bool TryLoadIfExists(out McpGuiSettings? settings, out string? error)
	{
		settings = null;
		error = null;

		if (!File.Exists(ConfigPath))
			return true;

		try
		{
			settings = Load();
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException)
		{
			error = $"MCP GUI settings could not be loaded: {ex.Message}";
			return false;
		}
	}

	public McpGuiSettings Load()
	{
		var settings = JsonSerializer.Deserialize<McpGuiSettings>(File.ReadAllText(ConfigPath), JsonOptions)
			?? throw new InvalidOperationException("MCP GUI settings must be a JSON object.");

		Normalize(settings);
		return settings;
	}

	public void Save(McpGuiSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		Normalize(settings);

		var directory = Path.GetDirectoryName(ConfigPath);
		if (!string.IsNullOrWhiteSpace(directory))
			Directory.CreateDirectory(directory);

		File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, JsonOptions));
	}

	private static void Normalize(McpGuiSettings settings)
	{
		if (settings.SchemaVersion != 1)
			throw new InvalidOperationException("schemaVersion must be 1.");

		settings.Target ??= new McpGuiTargetSettings();
		settings.Policy ??= new McpGuiPolicySettings();
		settings.VirtualPointer ??= new McpGuiVirtualPointerSettings();

		settings.Target.AttachPidText ??= string.Empty;
		settings.VirtualPointer.HideDelayMs ??= "800";
	}
}
