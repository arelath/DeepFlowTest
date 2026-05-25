namespace DeepFlowTest.Mcp.ViewModels;

using System.Text.Json;
using DeepFlowTest.Mcp.Activity;

internal sealed class ActivityEventViewModel
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
	};

	public ActivityEventViewModel(McpActivityEvent activity)
	{
		Activity = activity;
	}

	public McpActivityEvent Activity { get; }

	public string TimeText => Activity.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");

	public string Source => Activity.Source;

	public string Kind => Activity.Kind;

	public string Name => Activity.Name;

	public string Status => Activity.Status;

	public string? Summary => Activity.Summary;

	public string DetailsText => JsonSerializer.Serialize(Activity, JsonOptions);
}
