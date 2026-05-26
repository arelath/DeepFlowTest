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

	public string DetailsText => Activity.Details is ToolActivityDetails tool
		? FormatToolDetails(tool)
		: JsonSerializer.Serialize(Activity, JsonOptions);

	private string FormatToolDetails(ToolActivityDetails details)
	{
		var builder = new System.Text.StringBuilder();
		builder.AppendLine($"Tool: {Activity.Name}");
		builder.AppendLine($"Event: {Activity.Kind}");
		builder.AppendLine($"Status: {Activity.Status}");
		if (Activity.Duration is not null)
			builder.AppendLine($"Duration: {Activity.Duration.Value.TotalMilliseconds:0} ms");
		if (!string.IsNullOrWhiteSpace(Activity.Summary))
			builder.AppendLine($"Summary: {Activity.Summary}");

		builder.AppendLine();
		builder.AppendLine("Parameters:");
		builder.AppendLine(Serialize(details.Parameters));

		if (Activity.Kind.EndsWith(".success", System.StringComparison.Ordinal))
		{
			builder.AppendLine();
			builder.AppendLine("Result:");
			builder.AppendLine(Serialize(details.Result));
		}
		else if (Activity.Kind.EndsWith(".failure", System.StringComparison.Ordinal))
		{
			builder.AppendLine();
			builder.AppendLine("Error:");
			builder.AppendLine(Serialize(details.Error));
		}

		return builder.ToString();
	}

	private static string Serialize(object? value) =>
		value is null ? "{}" : JsonSerializer.Serialize(value, JsonOptions);
}
