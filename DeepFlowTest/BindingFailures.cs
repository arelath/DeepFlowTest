namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using DeepFlowTest.Contracts;

public sealed class BindingFailureOptions
{
	private IReadOnlyList<BindingFailureFilter> ignore = Array.Empty<BindingFailureFilter>();

	public TimeSpan StreamInterval { get; init; } = TimeSpan.FromMilliseconds(TimeoutDefaults.BindingFailureStreamIntervalMs);

	public int MaxBufferedFailures { get; init; } = 1000;

	public BindingFailureSeverity MinimumSeverity { get; init; } = BindingFailureSeverity.Warning;

	public bool IncludeExistingFailures { get; init; }

	public bool AssertOnDispose { get; init; } = true;

	public IReadOnlyList<BindingFailureFilter> Ignore
	{
		get => ignore;
		init => ignore = new ReadOnlyCollection<BindingFailureFilter>((value ?? throw new ArgumentNullException(nameof(Ignore))).ToArray());
	}

	internal void Validate()
	{
		_ = DurationUtility.ToMilliseconds(StreamInterval, nameof(StreamInterval));
		if (MaxBufferedFailures <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxBufferedFailures), MaxBufferedFailures, "The failure buffer size must be greater than zero.");
		if (ignore.Any(static filter => filter is null))
			throw new ArgumentException("Ignored binding failure filters cannot contain null entries.", nameof(Ignore));
		foreach (var filter in ignore)
			filter.Validate();
	}
}

public sealed class BindingFailureFilter
{
	public string Pattern { get; init; } = string.Empty;

	public BindingFailureFilterMode Mode { get; init; } = BindingFailureFilterMode.Contains;

	public bool IgnoreCase { get; init; } = true;

	public static BindingFailureFilter Contains(string value) =>
		new() { Pattern = value ?? string.Empty, Mode = BindingFailureFilterMode.Contains };

	public static BindingFailureFilter Exact(string value) =>
		new() { Pattern = value ?? string.Empty, Mode = BindingFailureFilterMode.Exact };

	public static BindingFailureFilter Regex(string pattern) =>
		new() { Pattern = pattern ?? string.Empty, Mode = BindingFailureFilterMode.Regex };

	internal void Validate()
	{
		if (Mode == BindingFailureFilterMode.Regex && !string.IsNullOrEmpty(Pattern))
			_ = new Regex(Pattern, IgnoreCase ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant);
	}

	internal bool IsMatch(string message)
	{
		if (string.IsNullOrEmpty(Pattern))
			return false;

		var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		return Mode switch
		{
			BindingFailureFilterMode.Exact => string.Equals(message, Pattern, comparison),
			BindingFailureFilterMode.Regex => System.Text.RegularExpressions.Regex.IsMatch(
				message,
				Pattern,
				IgnoreCase ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant),
			_ => message?.IndexOf(Pattern, comparison) >= 0,
		};
	}
}

public enum BindingFailureFilterMode
{
	Contains,
	Exact,
	Regex,
}

public sealed class BindingFailureEventArgs : EventArgs
{
	public BindingFailureEventArgs(BindingFailureDto failure, bool isIgnored)
	{
		Failure = failure ?? throw new ArgumentNullException(nameof(failure));
		IsIgnored = isIgnored;
	}

	public BindingFailureDto Failure { get; }

	public bool IsIgnored { get; }
}
