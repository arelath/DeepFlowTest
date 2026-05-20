namespace DeepFlowTest.Recorder;

using System;
using System.Text;
using DeepFlowTest;

internal static class RecorderFailureFormatter
{
	public static RecorderFailure Format(Exception exception)
	{
		_ = exception ?? throw new ArgumentNullException(nameof(exception));
		return new RecorderFailure(BuildStatus(exception), BuildDetails(exception));
	}

	private static string BuildStatus(Exception exception)
	{
		if (exception is AppConnectionException { InnerException: { } inner })
			return Truncate($"Target injection failed: {FirstNonEmptyLine(inner.Message)}", 240);

		return Truncate(FirstNonEmptyLine(exception.Message), 240);
	}

	private static string BuildDetails(Exception exception)
	{
		var builder = new StringBuilder();
		builder.AppendLine("Exception chain:");
		var current = exception;
		var level = 0;
		while (current is not null)
		{
			builder.AppendLine($"{level + 1}. {current.GetType().FullName}:");
			builder.AppendLine(current.Message);
			current = current.InnerException;
			level++;
			if (current is not null)
				builder.AppendLine();
		}

		if (exception is AppConnectionException { StartupLogTail: { Length: > 0 } startupLogTail })
		{
			builder.AppendLine();
			builder.AppendLine("Startup diagnostics:");
			builder.AppendLine(startupLogTail);
		}

		if (!string.IsNullOrWhiteSpace(exception.StackTrace))
		{
			builder.AppendLine();
			builder.AppendLine("Stack trace:");
			builder.AppendLine(exception.StackTrace);
		}

		return builder.ToString().TrimEnd();
	}

	private static string FirstNonEmptyLine(string value)
	{
		var lines = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
		foreach (var line in lines)
			if (!string.IsNullOrWhiteSpace(line))
				return line.Trim();

		return "Operation failed.";
	}

	private static string Truncate(string value, int maxLength)
	{
		if (value.Length <= maxLength)
			return value;

		return string.Concat(value.AsSpan(0, maxLength - 3), "...");
	}
}

internal sealed record RecorderFailure(string Status, string Details);
