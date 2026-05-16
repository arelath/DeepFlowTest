namespace DeepFlowTest.AppDriverPayload.Diagnostics;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DeepFlowTest.Contracts;

internal sealed class BindingFailureCaptureService
{
	private const int DefaultMaxStoredFailures = 1000;
	private const string ListenerName = "DeepFlowTest.BindingFailureTraceListener";

	private readonly object sync = new();
	private readonly Queue<BindingFailureDto> failures = new();
	private BindingFailureTraceListener? listener;
	private SourceLevels? originalLevel;
	private int activeRegistrations;
	private int maxStoredFailures = DefaultMaxStoredFailures;
	private long sequenceNumber;
	private string? lastMessage;
	private int? lastEventId;
	private BindingFailureSeverity lastSeverity;
	private DateTimeOffset lastCapturedUtc;

	public static BindingFailureCaptureService Instance { get; } = new();

	public int ActiveRegistrationCount
	{
		get
		{
			lock (sync)
				return activeRegistrations;
		}
	}

	public IDisposable Start(BindingFailureCaptureSettings settings)
	{
		settings ??= new BindingFailureCaptureSettings();
		lock (sync)
		{
			maxStoredFailures = activeRegistrations == 0
				? Math.Max(1, settings.MaxStoredFailures)
				: Math.Max(maxStoredFailures, Math.Max(1, settings.MaxStoredFailures));
			TrimToMaxStoredFailures();
			if (activeRegistrations == 0)
				InstallListener(settings.MinimumSeverity);
			else
				EnsureTraceLevel(settings.MinimumSeverity);

			activeRegistrations++;
		}

		return new Registration(this);
	}

	public BindingFailureBatchDto ReadSince(long? afterSequenceNumber, int maxCount)
	{
		lock (sync)
		{
			var currentSequence = sequenceNumber;
			if (!afterSequenceNumber.HasValue || maxCount <= 0)
			{
				return new BindingFailureBatchDto
				{
					LastSequenceNumber = currentSequence,
					Failures = [],
				};
			}

			var after = Math.Max(0, afterSequenceNumber.Value);
			var oldestSequence = failures.Count == 0
				? currentSequence + 1
				: failures.Peek().SequenceNumber;
			var dropped = after < oldestSequence - 1
				? (int)Math.Min(int.MaxValue, oldestSequence - 1 - after)
				: 0;
			var selected = failures
				.Where(failure => failure.SequenceNumber > after)
				.Take(Math.Max(1, maxCount))
				.ToArray();

			return new BindingFailureBatchDto
			{
				LastSequenceNumber = selected.Length == 0 ? currentSequence : selected[selected.Length - 1].SequenceNumber,
				DroppedCount = dropped,
				Failures = selected,
			};
		}
	}

	internal void Record(
		BindingFailureSeverity severity,
		string? rawMessage,
		string? source = null,
		int? eventId = null)
	{
		var normalized = Normalize(rawMessage);
		if (string.IsNullOrWhiteSpace(normalized))
			return;

		var now = DateTimeOffset.UtcNow;
		lock (sync)
		{
			if (IsDuplicate(now, severity, eventId, normalized))
				return;

			sequenceNumber++;
			failures.Enqueue(new BindingFailureDto
			{
				SequenceNumber = sequenceNumber,
				TimestampUtc = now,
				Severity = severity,
				Message = normalized,
				RawMessage = rawMessage ?? string.Empty,
				Source = source ?? string.Empty,
				EventId = eventId,
				ManagedThreadId = Environment.CurrentManagedThreadId,
			});
			TrimToMaxStoredFailures();

			lastMessage = normalized;
			lastEventId = eventId;
			lastSeverity = severity;
			lastCapturedUtc = now;
		}
	}

	internal void ResetForTests()
	{
		lock (sync)
		{
			RemoveListener();
			activeRegistrations = 0;
			maxStoredFailures = DefaultMaxStoredFailures;
			failures.Clear();
			sequenceNumber = 0;
			lastMessage = null;
			lastEventId = null;
			lastSeverity = default;
			lastCapturedUtc = default;
		}
	}

	private static SourceLevels ToSourceLevels(BindingFailureSeverity severity) =>
		severity switch
		{
			BindingFailureSeverity.Error => SourceLevels.Error,
			BindingFailureSeverity.Warning => SourceLevels.Warning,
			BindingFailureSeverity.Information => SourceLevels.Information,
			BindingFailureSeverity.Verbose => SourceLevels.Verbose,
			_ => SourceLevels.Warning,
		};

	private static string Normalize(string? message) =>
		string.Join(
			" ",
			(message ?? string.Empty)
				.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

	private void InstallListener(BindingFailureSeverity minimumSeverity)
	{
		var source = PresentationTraceSources.DataBindingSource;
		originalLevel = source.Switch.Level;
		listener = new BindingFailureTraceListener(this) { Name = ListenerName };
		source.Listeners.Add(listener);
		EnsureTraceLevel(minimumSeverity);
	}

	private void EnsureTraceLevel(BindingFailureSeverity minimumSeverity)
	{
		var source = PresentationTraceSources.DataBindingSource;
		var requested = ToSourceLevels(minimumSeverity);
		if ((int)source.Switch.Level < (int)requested)
			source.Switch.Level = requested;
	}

	private void StopRegistration()
	{
		lock (sync)
		{
			if (activeRegistrations <= 0)
				return;

			activeRegistrations--;
			if (activeRegistrations == 0)
				RemoveListener();
		}
	}

	private void RemoveListener()
	{
		var source = PresentationTraceSources.DataBindingSource;
		if (listener is not null)
		{
			source.Listeners.Remove(listener);
			listener.Dispose();
			listener = null;
		}

		if (originalLevel.HasValue)
		{
			source.Switch.Level = originalLevel.Value;
			originalLevel = null;
		}
	}

	private bool IsDuplicate(DateTimeOffset now, BindingFailureSeverity severity, int? eventId, string message) =>
		string.Equals(message, lastMessage, StringComparison.Ordinal)
		&& eventId == lastEventId
		&& severity == lastSeverity
		&& (now - lastCapturedUtc).TotalMilliseconds < TimeoutDefaults.BindingFailureDuplicateSuppressionMs;

	private void TrimToMaxStoredFailures()
	{
		while (failures.Count > maxStoredFailures)
		{
			failures.Dequeue();
		}
	}

	private sealed class Registration : IDisposable
	{
		private BindingFailureCaptureService? owner;

		public Registration(BindingFailureCaptureService owner)
		{
			this.owner = owner;
		}

		public void Dispose()
		{
			var current = owner;
			if (current is null)
				return;

			owner = null;
			current.StopRegistration();
		}
	}
}

internal sealed class BindingFailureCaptureSettings
{
	public int MaxStoredFailures { get; set; } = 1000;

	public BindingFailureSeverity MinimumSeverity { get; set; } = BindingFailureSeverity.Warning;
}
