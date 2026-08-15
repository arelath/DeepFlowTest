namespace DeepFlowTest.Automation;

using System;
using System.Diagnostics;
using System.Threading;
using DeepFlowTest.Interop;

public sealed record WaitExecutionOptions(int TimeoutMs, int IntervalMs, int RequiredMatches)
{
	public WaitExecutionOptions Validate()
	{
		if (TimeoutMs <= 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait timeout must be greater than zero.");
		if (IntervalMs <= 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait interval must be greater than zero.");
		if (RequiredMatches <= 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait match count must be greater than zero.");
		return this;
	}
}

public sealed class WaitExecutor(FindSnapshotService? findService = null)
{
	private readonly FindSnapshotService findService = findService ?? new FindSnapshotService();

	public FindResultData Execute(
		Func<VisualTreeSnapshot> readSnapshot,
		FindSnapshotOptions findOptions,
		WaitExecutionOptions executionOptions,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(readSnapshot);
		ArgumentNullException.ThrowIfNull(findOptions);
		_ = (executionOptions ?? throw new ArgumentNullException(nameof(executionOptions))).Validate();

		var stopwatch = Stopwatch.StartNew();
		try
		{
			while (stopwatch.ElapsedMilliseconds <= executionOptions.TimeoutMs)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var result = findService.Find(readSnapshot(), findOptions);
				if (result.MatchCount >= executionOptions.RequiredMatches)
					return result;

				var remaining = executionOptions.TimeoutMs - (int)stopwatch.ElapsedMilliseconds;
				if (remaining <= 0)
					break;

				if (cancellationToken.WaitHandle.WaitOne(Math.Min(executionOptions.IntervalMs, remaining)))
					cancellationToken.ThrowIfCancellationRequested();
			}
		}
		catch (OperationCanceledException)
		{
			throw new AutomationException(AutomationErrorCodes.CommandTimeout, "Wait was canceled.");
		}

		throw new AutomationException(AutomationErrorCodes.CommandTimeout, $"Wait timed out after {executionOptions.TimeoutMs} ms.");
	}
}
