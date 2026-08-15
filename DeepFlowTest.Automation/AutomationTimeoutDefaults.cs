namespace DeepFlowTest.Automation;

using DeepFlowTest.Contracts;

public static class AutomationTimeoutDefaults
{
	public const int AttachTimeoutMs = TimeoutDefaults.CliAttachTimeoutMs;

	public const int AttachRetrySleepMs = TimeoutDefaults.CliAttachRetrySleepMs;

	public const int OneShotConnectTimeoutCapMs = TimeoutDefaults.CliOneShotConnectTimeoutCapMs;
}
