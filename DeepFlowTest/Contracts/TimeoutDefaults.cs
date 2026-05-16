namespace DeepFlowTest.Contracts;

using System;

public static class TimeoutDefaults
{
	public const int CommandTimeoutMs = 10_000;
	public const int ElementQueryTimeoutMs = 30_000;
	public const int AssertionTimeoutMs = 5_000;
	public const int AssertionPollDelayMs = 100;

	public const int AppDriverTimeoutMs = CommandTimeoutMs;
	public static TimeSpan AppDriverTimeout => TimeSpan.FromMilliseconds(AppDriverTimeoutMs);

	public const int ElementPollFallbackDelayMs = 1_000;
	public static int[] CreateElementPollBackoffMs() => [25, 100, 500, 1000, 2000];

	public const int NamedPipeConnectTimeoutMs = 5_000;
	public const int NamedPipeConnectRetryCount = 2;
	public const int PipeProbeConnectTimeoutMs = 100;

	public const int CliCommandTimeoutMs = CommandTimeoutMs;
	public const int CliAttachTimeoutMs = CommandTimeoutMs;
	public const int CliAttachRetrySleepMs = 100;
	public const int CliOneShotConnectTimeoutCapMs = 500;
	public const int CliWaitIntervalMs = 250;

	public const int KeyboardDelayMs = 50;
	public const int KeyboardTextInputDelayMs = 20;
	public const int KeyboardPhysicalDelayCapMs = 50;

	public const int PayloadCommandTimeoutMs = 1_000;
	public const int PayloadLargeCommandTimeoutMs = 5_000;
	public const int PayloadNativeDialogGraceMs = 250;
	public const int PayloadModalPollDelayMs = 50;

	public const int StreamIntervalMs = 1_000;
	public const int StreamMinimumIntervalMs = 50;
	public const int StreamStopTimeoutMs = 2_000;
	public const int StreamCleanupTimeoutMs = 500;

	public const int BindingFailureStreamIntervalMs = 100;
	public const int BindingFailureStreamMinimumIntervalMs = StreamMinimumIntervalMs;
	public const int BindingFailureStopTimeoutMs = StreamStopTimeoutMs;
	public const int BindingFailureReaderShutdownTimeoutMs = 500;
	public const int BindingFailureDuplicateSuppressionMs = 100;

	public const int ScreenshotStableTimeoutMs = 5_000;
	public const int ScreenshotStablePollDelayMs = 500;

	public const int PayloadCrashLogWaitMs = 250;
	public const int PayloadCrashLogPollDelayMs = 25;
}
