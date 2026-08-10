namespace DeepFlowTest;

using System;

public sealed class DragAndDropOptions
{
	public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(500);

	public TimeSpan HoldDuration { get; init; } = TimeSpan.FromMilliseconds(75);

	public TimeSpan StepInterval { get; init; } = TimeSpan.FromMilliseconds(16);

	public TimeSpan PostDropDelay { get; init; } = TimeSpan.FromMilliseconds(100);

	public double SourceAnchorX { get; init; } = 0.5;

	public double SourceAnchorY { get; init; } = 0.5;

	public double DestinationAnchorX { get; init; } = 0.5;

	public double DestinationAnchorY { get; init; } = 0.5;

	public bool UseInjectedEvents { get; init; } = true;

	public bool EnsureForeground { get; init; }

	public bool ValidateSameProcess { get; init; } = true;

	public TimeSpan? Timeout { get; init; }

	internal void Validate()
	{
		_ = DurationUtility.ToMilliseconds(Duration, nameof(Duration), allowZero: true);
		_ = DurationUtility.ToMilliseconds(HoldDuration, nameof(HoldDuration), allowZero: true);
		_ = DurationUtility.ToMilliseconds(StepInterval, nameof(StepInterval));
		_ = DurationUtility.ToMilliseconds(PostDropDelay, nameof(PostDropDelay), allowZero: true);
		if (Timeout is TimeSpan timeout)
			_ = DurationUtility.ToMilliseconds(timeout, nameof(Timeout));
		ValidateAnchor(SourceAnchorX, nameof(SourceAnchorX));
		ValidateAnchor(SourceAnchorY, nameof(SourceAnchorY));
		ValidateAnchor(DestinationAnchorX, nameof(DestinationAnchorX));
		ValidateAnchor(DestinationAnchorY, nameof(DestinationAnchorY));
	}

	private static void ValidateAnchor(double value, string parameterName)
	{
		if (double.IsNaN(value) || value < 0 || value > 1)
			throw new ArgumentOutOfRangeException(parameterName, value, "Anchor values must be between zero and one.");
	}
}
