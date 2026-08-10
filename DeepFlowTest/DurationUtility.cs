namespace DeepFlowTest;

using System;

internal static class DurationUtility
{
	public static int ToMilliseconds(TimeSpan value, string parameterName, bool allowZero = false)
	{
		if (value < TimeSpan.Zero || (!allowZero && value == TimeSpan.Zero))
			throw new ArgumentOutOfRangeException(parameterName, value, allowZero ? "The duration cannot be negative." : "The duration must be greater than zero.");
		if (value.TotalMilliseconds > int.MaxValue)
			throw new ArgumentOutOfRangeException(parameterName, value, $"The duration cannot exceed {int.MaxValue} milliseconds.");

		return allowZero && value == TimeSpan.Zero ? 0 : Math.Max(1, (int)Math.Ceiling(value.TotalMilliseconds));
	}

	public static TimeSpan PositiveOrDefault(TimeSpan? value, TimeSpan defaultValue, string parameterName)
	{
		var effective = value ?? defaultValue;
		_ = ToMilliseconds(effective, parameterName);
		return effective;
	}
}
