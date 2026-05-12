namespace DeepFlowTest.Cli;

using System;

public sealed class ActionGate
{
	private readonly Func<string, string?> getEnvironmentVariable;

	public ActionGate(Func<string, string?>? getEnvironmentVariable = null)
	{
		this.getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
	}

	public void Demand(string actionName, CliCommonOptions options, bool arbitraryInvoke = false)
	{
		_ = options ?? throw new ArgumentNullException(nameof(options));

		if (arbitraryInvoke && !options.AllowArbitraryInvoke)
			throw new CliException(CliErrorCodes.ArbitraryInvokeDenied, "Arbitrary invoke requires --allow-arbitrary-invoke.");

		if (IsStrictModeEnabled() && !options.AllowActions)
			throw new CliException(CliErrorCodes.ActionDenied, $"Strict action mode requires --allow-actions for '{actionName}'.");
	}

	private bool IsStrictModeEnabled()
	{
		var value = getEnvironmentVariable("DEEPFLOWTEST_CLI_STRICT_ACTIONS");
		return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
	}
}
