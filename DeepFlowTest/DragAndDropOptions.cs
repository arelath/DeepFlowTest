namespace DeepFlowTest;

public sealed class DragAndDropOptions
{
	public int DurationMs { get; set; } = 500;

	public int HoldMs { get; set; } = 75;

	public int StepIntervalMs { get; set; } = 16;

	public int PostDropWaitMs { get; set; } = 100;

	public double SourceAnchorX { get; set; } = 0.5;

	public double SourceAnchorY { get; set; } = 0.5;

	public double DestinationAnchorX { get; set; } = 0.5;

	public double DestinationAnchorY { get; set; } = 0.5;

	public bool EnsureForeground { get; set; } = true;

	public bool ValidateSameProcess { get; set; } = true;

	public int? TimeoutMs { get; set; }
}
