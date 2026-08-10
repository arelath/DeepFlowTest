namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Assert.TestFrameworks;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal sealed class BindingFailureMonitor : IDisposable
{
	private readonly object sync = new();
	private readonly IUnsafeAppDriverCommandSession session;
	private readonly int timeoutMs;
	private BindingFailureOptions options = new();
	private readonly List<BindingFailureDto> observedFailures = [];
	private IAppDriverStreamSession? streamSession;
	private CancellationTokenSource? cancellation;
	private Task? readerTask;
	private long lastSequenceNumber;
	private int leaseCount;
	private int droppedCount;
	private Exception? backgroundError;
	private bool disposed;

	public BindingFailureMonitor(IUnsafeAppDriverCommandSession session, AppDriverOptions driverOptions)
	{
		this.session = session ?? throw new ArgumentNullException(nameof(session));
		_ = driverOptions ?? throw new ArgumentNullException(nameof(driverOptions));
		timeoutMs = (int)Math.Max(1, driverOptions.Timeout.TotalMilliseconds);
	}

	public event EventHandler<BindingFailureEventArgs>? FailureReceived;

	public bool IsStarted
	{
		get
		{
			lock (sync)
				return streamSession is not null;
		}
	}

	public IDisposable Start(BindingFailureOptions bindingOptions)
	{
		_ = bindingOptions ?? throw new ArgumentNullException(nameof(bindingOptions));
		lock (sync)
		{
			ThrowIfDisposed();
			if (leaseCount == 0)
				StartCore(Clone(bindingOptions));

			leaseCount++;
		}

		return new Lease(this);
	}

	public void CheckpointAndThrowIfNeeded()
	{
		if (!IsStarted)
			return;

		Checkpoint();
		ThrowIfBackgroundError();
		AssertObservedFailures(clear: true);
	}

	public void AssertNoFailures(bool clear)
	{
		if (IsStarted)
			Checkpoint();

		ThrowIfBackgroundError();
		AssertObservedFailures(clear);
	}

	public IReadOnlyList<BindingFailureDto> GetObservedFailures()
	{
		lock (sync)
			return observedFailures.ToArray();
	}

	public void Clear()
	{
		lock (sync)
		{
			observedFailures.Clear();
			droppedCount = 0;
			backgroundError = null;
		}
	}

	public void Dispose()
	{
		lock (sync)
		{
			if (disposed)
				return;

			disposed = true;
			leaseCount = 0;
		}

		StopCore();
	}

	private void StartCore(BindingFailureOptions bindingOptions)
	{
		if (session is not IAppDriverStreamingSession streamingSession)
		{
			throw new AppDriverException(
				ProtocolConstants.ErrorCodes.UnsupportedProtocol,
				"The current AppDriver command session does not support streaming diagnostics.");
		}

		options = bindingOptions;
		backgroundError = null;
		if (options.IncludeExistingFailures)
			lastSequenceNumber = 0;
		else
			lastSequenceNumber = ReadCheckpoint(afterSequenceNumber: null, maxCount: 0).LastSequenceNumber;

		options.Validate();
		var intervalMs = Math.Max(TimeoutDefaults.BindingFailureStreamMinimumIntervalMs, DurationUtility.ToMilliseconds(options.StreamInterval, nameof(options.StreamInterval)));
		var request = new StartSendingCommandRequest
		{
			StreamKind = ProtocolConstants.StreamKinds.BindingFailures,
			IntervalMs = intervalMs,
			TimeoutMs = timeoutMs,
		};
		streamSession = streamingSession.StartStream(request, timeoutMs);
		cancellation = new CancellationTokenSource();
		readerTask = Task.Run(() => ReadLoop(streamSession, intervalMs, cancellation.Token));
	}

	private void StopLease()
	{
		var shouldStop = false;
		lock (sync)
		{
			if (leaseCount <= 0)
				return;

			leaseCount--;
			shouldStop = leaseCount == 0;
		}

		if (shouldStop)
			StopCore();
	}

	private void StopCore()
	{
		IAppDriverStreamSession? stream;
		CancellationTokenSource? tokenSource;
		Task? task;
		lock (sync)
		{
			stream = streamSession;
			tokenSource = cancellation;
			task = readerTask;
			streamSession = null;
			cancellation = null;
			readerTask = null;
		}

		if (stream is null)
			return;

		tokenSource?.Cancel();
		try
		{
			if (!string.IsNullOrWhiteSpace(stream.Start.SubscriptionId))
			{
				session.Send<StopSendingCommandResponse>(new StopSendingCommandRequest
				{
					SubscriptionId = stream.Start.SubscriptionId,
					TimeoutMs = Math.Min(timeoutMs, TimeoutDefaults.BindingFailureStopTimeoutMs),
				});
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
		}
		finally
		{
			stream.Dispose();
			try
			{
				task?.Wait(TimeoutDefaults.BindingFailureReaderShutdownTimeoutMs);
			}
			catch (AggregateException ex) when (ex.InnerExceptions.All(static inner => inner is OperationCanceledException or TaskCanceledException))
			{
			}

			tokenSource?.Dispose();
		}
	}

	private void Checkpoint()
	{
		var after = GetLastSequenceNumber();
		ProcessBatch(ReadCheckpoint(after, Math.Max(1, options.MaxBufferedFailures)));
	}

	private BindingFailureBatchDto ReadCheckpoint(long? afterSequenceNumber, int maxCount) =>
		session.Send<BindingFailureBatchDto>(new GetBindingFailuresCommandRequest
		{
			AfterSequenceNumber = afterSequenceNumber,
			MaxCount = maxCount,
			TimeoutMs = timeoutMs,
		});

	private long GetLastSequenceNumber()
	{
		lock (sync)
			return lastSequenceNumber;
	}

	private void ReadLoop(IAppDriverStreamSession stream, int intervalMs, CancellationToken token)
	{
		var readTimeout = Math.Max(timeoutMs, intervalMs * 2);
		while (!token.IsCancellationRequested)
		{
			try
			{
				var frame = stream.ReadFrame(readTimeout, token);
				if (frame is null)
					continue;
				if (frame.Error is not null)
				{
					StoreBackgroundError(new AppDriverException(frame.Error.Code, frame.Error.Message));
					return;
				}
				if (frame.Data is null)
					continue;

				ProcessBatch(MessagePacker.ConvertTo<BindingFailureBatchDto>(frame.Data));
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				if (!token.IsCancellationRequested)
					StoreBackgroundError(ex);
				return;
			}
		}
	}

	private void ProcessBatch(BindingFailureBatchDto batch)
	{
		if (batch is null)
			return;

		List<BindingFailureEventArgs> events = [];
		lock (sync)
		{
			var previousSequence = lastSequenceNumber;
			droppedCount += Math.Max(0, batch.DroppedCount);
			foreach (var failure in batch.Failures ?? [])
			{
				if (failure.SequenceNumber <= previousSequence)
					continue;

				var ignored = ShouldIgnore(failure);
				events.Add(new BindingFailureEventArgs(failure, ignored));
				if (!ignored)
				{
					observedFailures.Add(failure);
					TrimObservedFailures();
				}

				if (failure.SequenceNumber > lastSequenceNumber)
					lastSequenceNumber = failure.SequenceNumber;
			}

			if (batch.LastSequenceNumber > lastSequenceNumber)
				lastSequenceNumber = batch.LastSequenceNumber;
		}

		foreach (var args in events)
			FailureReceived?.Invoke(this, args);
	}

	private bool ShouldIgnore(BindingFailureDto failure)
	{
		if (failure.Severity < options.MinimumSeverity)
			return true;

		var message = failure.Message ?? string.Empty;
		foreach (var filter in options.Ignore)
			if (filter.IsMatch(message))
				return true;

		return false;
	}

	private void TrimObservedFailures()
	{
		var maxFailures = Math.Max(1, options.MaxBufferedFailures);
		while (observedFailures.Count > maxFailures)
		{
			observedFailures.RemoveAt(0);
			droppedCount++;
		}
	}

	private void AssertObservedFailures(bool clear)
	{
		BindingFailureDto[] failures;
		int dropped;
		lock (sync)
		{
			failures = observedFailures.ToArray();
			dropped = droppedCount;
			if (failures.Length == 0 && dropped == 0)
				return;
			if (clear)
			{
				observedFailures.Clear();
				droppedCount = 0;
			}
		}

		TestFrameworkProvider.Throw(BuildAssertionMessage(failures, dropped));
	}

	private void ThrowIfBackgroundError()
	{
		Exception? error;
		lock (sync)
			error = backgroundError;

		if (error is not null)
			throw new AppDriverException(ProtocolConstants.ErrorCodes.ProtocolError, "Binding failure stream failed.", error);
	}

	private void StoreBackgroundError(Exception ex)
	{
		lock (sync)
			backgroundError ??= ex;
	}

	private static string BuildAssertionMessage(IReadOnlyList<BindingFailureDto> failures, int dropped)
	{
		var builder = new StringBuilder();
		builder.Append("WPF binding failures detected");
		if (failures.Count > 0)
			builder.Append(" (").Append(failures.Count).Append(")");
		builder.AppendLine(".");
		if (dropped > 0)
			builder.AppendLine($"{dropped} binding failure(s) were dropped before they could be reported.");

		foreach (var failure in failures.Take(10))
		{
			builder
				.Append("- ")
				.Append(failure.TimestampUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
				.Append(' ')
				.Append(ProtocolValueMapper.FormatBindingFailureSeverity(failure.Severity));
			if (failure.EventId.HasValue)
				builder.Append(" event ").Append(failure.EventId.Value);
			builder.Append(": ").AppendLine(failure.Message);
		}

		if (failures.Count > 10)
			builder.AppendLine($"{failures.Count - 10} additional binding failure(s) were omitted.");

		return builder.ToString().TrimEnd();
	}

	private static BindingFailureOptions Clone(BindingFailureOptions source)
	{
		return new BindingFailureOptions
		{
			StreamInterval = source.StreamInterval,
			MaxBufferedFailures = source.MaxBufferedFailures,
			MinimumSeverity = source.MinimumSeverity,
			IncludeExistingFailures = source.IncludeExistingFailures,
			AssertOnDispose = source.AssertOnDispose,
			Ignore = source.Ignore,
		};
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
			throw new ObjectDisposedException(nameof(BindingFailureMonitor));
	}

	private sealed class Lease : IDisposable
	{
		private BindingFailureMonitor? owner;

		public Lease(BindingFailureMonitor owner)
		{
			this.owner = owner;
		}

		public void Dispose()
		{
			var current = owner;
			if (current is null)
				return;

			owner = null;
			current.StopLease();
		}
	}
}
