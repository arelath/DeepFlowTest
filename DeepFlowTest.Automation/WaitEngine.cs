namespace DeepFlowTest.Automation;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;

public sealed class WaitEngine
{
	public async Task<WaitResult> WaitAsync(
		IAutomationSession session,
		WaitRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(request);
		Validate(request);

		var stopwatch = Stopwatch.StartNew();
		string? titleBaseline = null;
		if (request.Condition is WindowTitleChangedWaitCondition titleCondition)
		{
			cancellationToken.ThrowIfCancellationRequested();
			titleBaseline = titleCondition.InitialTitle ?? request.ObservationSource.ReadWindowTitle();
		}

		string? previousFingerprint = null;
		long stableSinceMs = 0;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var remaining = Remaining(request.TimeoutMs, stopwatch.ElapsedMilliseconds);
			if (remaining < 0)
				break;

			var commandTimeout = Math.Max(1, remaining);
			WaitResult? result = null;
			var observationTimedOut = false;
			using var observationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			observationCancellation.CancelAfter(commandTimeout);
			try
			{
				if (request.Condition is ResponsiveWaitCondition)
				{
					var probeTimeout = Math.Min(request.IntervalMs, commandTimeout);
					observationCancellation.CancelAfter(probeTimeout);
					result = await TryResponsiveAsync(session, request, stopwatch, probeTimeout, observationCancellation.Token).ConfigureAwait(false);
				}
				else if (request.Condition is WindowTitleChangedWaitCondition)
				{
					var currentTitle = request.ObservationSource.ReadWindowTitle();
					result = string.Equals(currentTitle, titleBaseline, StringComparison.Ordinal)
						? null
						: Success(request, stopwatch, windowTitle: currentTitle);
				}
				else
				{
					var snapshot = await request.ObservationSource.ReadSnapshotAsync(
						session,
						request.Snapshot!,
						commandTimeout,
						observationCancellation.Token).ConfigureAwait(false);
					if (request.Condition is StableWaitCondition stable)
					{
						var fingerprint = stable.Fingerprint.Compute(snapshot);
						if (!string.Equals(fingerprint, previousFingerprint, StringComparison.Ordinal))
						{
							previousFingerprint = fingerprint;
							stableSinceMs = stopwatch.ElapsedMilliseconds;
							result = null;
						}
						else
						{
							result = stopwatch.ElapsedMilliseconds - stableSinceMs >= stable.StabilityMs
								? Success(request, stopwatch, revision: snapshot.SequenceNumber, matchCount: snapshot.NodeCount)
								: null;
						}
					}
					else
					{
						var matches = ((ElementWaitCondition)request.Condition).Target.Find(snapshot);
						result = IsSatisfied(request.Condition, matches)
							? Success(request, stopwatch, snapshot.SequenceNumber, matches.MatchCount, matches)
							: null;
					}
				}
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				observationTimedOut = request.Condition is not ResponsiveWaitCondition;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw new OperationCanceledException(cancellationToken);
			}

			cancellationToken.ThrowIfCancellationRequested();
			if (observationTimedOut)
				break;
			if (result is not null)
				return result;

			remaining = Remaining(request.TimeoutMs, stopwatch.ElapsedMilliseconds);
			if (remaining <= 0)
				break;

			try
			{
				await Task.Delay(Math.Min(request.IntervalMs, remaining), cancellationToken).ConfigureAwait(false);
			}
			catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw new OperationCanceledException(cancellationToken);
			}
		}

		throw new AutomationException(
			AutomationErrorCodes.CommandTimeout,
			$"Wait for {request.Condition.Kind} timed out after {request.TimeoutMs} ms.");
	}

	private static async Task<WaitResult?> TryResponsiveAsync(
		IAutomationSession session,
		WaitRequest request,
		Stopwatch stopwatch,
		int commandTimeoutMs,
		CancellationToken cancellationToken)
	{
		try
		{
			_ = await session.SendAsync<object>(
				new PingCommandRequest(commandTimeoutMs),
				commandTimeoutMs,
				cancellationToken).ConfigureAwait(false);
			return Success(request, stopwatch);
		}
		catch (AutomationException)
		{
			return null;
		}
	}

	private static bool IsSatisfied(WaitCondition condition, FindResultData result) => condition switch
	{
		ElementExistsWaitCondition => result.MatchCount > 0,
		ElementAbsentWaitCondition => result.MatchCount == 0,
		ElementExactCountWaitCondition exact => result.MatchCount == exact.Count,
		ElementMinimumCountWaitCondition minimum => result.MatchCount >= minimum.Count,
		ElementPropertyEqualsWaitCondition equals => AnyProperty(result, equals.PropertyName, equals.PropertyValue, equal: true),
		ElementPropertyDiffersWaitCondition differs => result.MatchCount > 0 && AnyProperty(result, differs.PropertyName, differs.PropertyValue, equal: false),
		ElementEnabledWaitCondition => AnyBoolean(result, KnownProperties.IsEnabled, expected: true),
		ElementDisabledWaitCondition => AnyBoolean(result, KnownProperties.IsEnabled, expected: false),
		ElementVisibleWaitCondition => AnyBoolean(result, KnownProperties.IsVisible, expected: true),
		ElementHiddenWaitCondition => result.MatchCount > 0 && AnyBoolean(result, KnownProperties.IsVisible, expected: false),
		_ => throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Unsupported wait condition: {condition.Kind}."),
	};

	private static bool AnyProperty(FindResultData result, string name, string expected, bool equal)
	{
		foreach (var match in result.Matches)
		{
			var hasValue = match.Node.Properties.TryGetValue(name, out var value);
			var isEqual = hasValue && string.Equals(
				Convert.ToString(value, CultureInfo.InvariantCulture),
				expected,
				StringComparison.OrdinalIgnoreCase);
			if (isEqual == equal)
				return true;
		}

		return false;
	}

	private static bool AnyBoolean(FindResultData result, string name, bool expected)
	{
		foreach (var match in result.Matches)
		{
			if (!match.Node.Properties.TryGetValue(name, out var value) || value is null)
				continue;
			if (value is bool boolValue && boolValue == expected)
				return true;
			if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed) && parsed == expected)
				return true;
		}

		return false;
	}

	private static WaitResult Success(
		WaitRequest request,
		Stopwatch stopwatch,
		long? revision = null,
		int matchCount = 0,
		FindResultData? matchResult = null,
		string? windowTitle = null) =>
		new()
		{
			Condition = request.Condition.Kind,
			ElapsedMs = stopwatch.ElapsedMilliseconds,
			Revision = revision ?? request.ObservationSource.LatestRevision,
			MatchCount = matchCount,
			MatchResult = matchResult,
			WindowTitle = windowTitle,
		};

	private static int Remaining(int timeoutMs, long elapsedMs) =>
		(int)Math.Max(int.MinValue, Math.Min(int.MaxValue, timeoutMs - elapsedMs));

	private static void Validate(WaitRequest request)
	{
		if (request.Condition is null)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait condition is required.");
		if (request.ObservationSource is null)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait observation source is required.");
		if (request.TimeoutMs <= 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait timeout must be greater than zero.");
		if (request.IntervalMs <= 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait interval must be greater than zero.");

		if (request.Condition is ElementWaitCondition element && element.Target is null)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait target is required.");
		if (request.Condition is ElementExactCountWaitCondition { Count: < 0 }
			or ElementMinimumCountWaitCondition { Count: < 0 })
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait match count cannot be negative.");
		if (request.Condition is ElementPropertyWaitCondition property && string.IsNullOrWhiteSpace(property.PropertyName))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait property name is required.");
		if (request.Condition is StableWaitCondition { StabilityMs: <= 0 })
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait stability must be greater than zero.");
		if (request.Condition is StableWaitCondition { Fingerprint: null })
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait snapshot fingerprint is required.");
		if (request.Condition is not (ResponsiveWaitCondition or WindowTitleChangedWaitCondition) && request.Snapshot is null)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait snapshot request is required for this condition.");
		if (request.Snapshot is { MaxNodeCount: <= 0 })
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait snapshot node count must be greater than zero.");
	}
}
