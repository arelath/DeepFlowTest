namespace DeepFlowTest.Cli;

using System.Collections.Generic;

public sealed class CliDefaults
{
	public int TimeoutMs { get; set; } = 10_000;

	public string OutputFormat { get; set; } = "json";

	public bool HideEmpty { get; set; } = true;

	public bool UseShortIds { get; set; } = true;

	public string AfterSnapshot { get; set; } = "none";

	public string TreeShape { get; set; } = "flat";

	public int TreeMaxDepth { get; set; } = -1;

	public int TreeLimit { get; set; } = 1000;

	public List<string> PropertyNames { get; set; } = new()
	{
		"Name",
		"AutomationProperties.Name",
		"AutomationProperties.AutomationId",
		"Text",
		"Content",
		"IsVisible",
		"IsEnabled",
	};

	public int FindLimit { get; set; } = 50;

	public int WaitIntervalMs { get; set; } = 250;

	public int WaitMatchCount { get; set; } = 1;

	public int StreamIntervalMs { get; set; } = 1000;

	public string ScreenshotFormat { get; set; } = "png";

	public int KeyDelayMs { get; set; } = 50;

	public bool EnsureForeground { get; set; } = true;
}
