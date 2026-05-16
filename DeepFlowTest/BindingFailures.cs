namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DeepFlowTest.Contracts;

public sealed class BindingFailureOptions
{
	public int StreamIntervalMs { get; set; } = TimeoutDefaults.BindingFailureStreamIntervalMs;

	public int MaxBufferedFailures { get; set; } = 1000;

	public BindingFailureSeverity MinimumSeverity { get; set; } = BindingFailureSeverity.Warning;

	public bool IncludeExistingFailures { get; set; }

	public bool AssertOnDispose { get; set; } = true;

	public List<BindingFailureFilter> Ignore { get; } = [];
}

public sealed class BindingFailureFilter
{
	public string Pattern { get; set; } = string.Empty;

	public BindingFailureFilterMode Mode { get; set; } = BindingFailureFilterMode.Contains;

	public bool IgnoreCase { get; set; } = true;

	public static BindingFailureFilter Contains(string value) =>
		new() { Pattern = value ?? string.Empty, Mode = BindingFailureFilterMode.Contains };

	public static BindingFailureFilter Exact(string value) =>
		new() { Pattern = value ?? string.Empty, Mode = BindingFailureFilterMode.Exact };

	public static BindingFailureFilter Regex(string pattern) =>
		new() { Pattern = pattern ?? string.Empty, Mode = BindingFailureFilterMode.Regex };

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
