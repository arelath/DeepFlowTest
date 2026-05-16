namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using DeepFlowTest.Contracts;

internal sealed class ElementWaiter(AppDriverOptions options)
{
	private readonly AppDriverOptions options = options ?? throw new ArgumentNullException(nameof(options));

	public Element PollForElement(
		Func<IReadOnlyList<Element>> find,
		string selectorDescription,
		TimeSpan? timeout = null,
		Func<string?>? noMatchDiagnostic = null)
	{
		_ = find ?? throw new ArgumentNullException(nameof(find));
		var effectiveTimeout = timeout ?? options.Timeout;
		var stopwatch = Stopwatch.StartNew();
		var attempt = 0;
		while (true)
		{
			SleepBeforePoll(attempt++, stopwatch, effectiveTimeout);

			var matches = find();
			if (matches.Count == 1)
				return matches[0];
			if (matches.Count > 1)
				throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, ElementDiagnosticFormatter.BuildAmbiguousElementMessage(selectorDescription, matches));
			if (stopwatch.Elapsed >= effectiveTimeout)
				break;
		}

		throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, ElementDiagnosticFormatter.BuildNoMatchElementMessage(selectorDescription, noMatchDiagnostic));
	}

	public IReadOnlyList<Element> PollForAny(Func<IReadOnlyList<Element>> find, TimeSpan timeout)
	{
		_ = find ?? throw new ArgumentNullException(nameof(find));
		var stopwatch = Stopwatch.StartNew();
		var attempt = 0;
		while (true)
		{
			SleepBeforePoll(attempt++, stopwatch, timeout);

			var matches = find();
			if (matches.Count != 0)
				return matches;
			if (stopwatch.Elapsed >= timeout)
				break;
		}

		return [];
	}

	private void SleepBeforePoll(int attempt, Stopwatch stopwatch, TimeSpan timeout)
	{
		if (attempt == 0)
			return;

		var remainingMs = (int)Math.Ceiling((timeout - stopwatch.Elapsed).TotalMilliseconds);
		if (remainingMs <= 0)
			return;

		Thread.Sleep(Math.Min(GetElementPollDelayMs(attempt), remainingMs));
	}

	private int GetElementPollDelayMs(int attempt)
	{
		var backoff = options.ElementPollBackoffMs ?? [];
		var index = attempt - 1;
		if (index >= 0 && index < backoff.Length)
			return Math.Max(0, backoff[index]);

		return TimeoutDefaults.ElementPollFallbackDelayMs;
	}
}
