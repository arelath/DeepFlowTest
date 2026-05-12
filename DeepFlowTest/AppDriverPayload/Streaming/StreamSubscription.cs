namespace DeepFlowTest.AppDriverPayload.Streaming;

using System;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;

public abstract class StreamSubscription : IDisposable
{
	private readonly CancellationTokenSource cancellation = new();
	private long sequenceNumber;
	private bool disposed;

	protected StreamSubscription(string subscriptionId, string streamKind, string? connectionId, int intervalMs, Func<object, bool> send)
	{
		SubscriptionId = string.IsNullOrWhiteSpace(subscriptionId) ? Guid.NewGuid().ToString("N") : subscriptionId;
		StreamKind = streamKind ?? throw new ArgumentNullException(nameof(streamKind));
		ConnectionId = connectionId;
		IntervalMs = Math.Max(50, intervalMs);
		Send = send ?? throw new ArgumentNullException(nameof(send));
	}

	public string SubscriptionId { get; }

	public string StreamKind { get; }

	public string? ConnectionId { get; }

	public int IntervalMs { get; }

	public long LastSequenceNumber => sequenceNumber;

	public Task? Completion { get; private set; }

	protected Func<object, bool> Send { get; }

	public void Start()
	{
		Completion ??= Task.Run(RunLoop);
	}

	public void Stop()
	{
		cancellation.Cancel();
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		cancellation.Cancel();
		cancellation.Dispose();
	}

	protected abstract object Capture(long sequenceNumber);

	private async Task RunLoop()
	{
		while (!cancellation.IsCancellationRequested)
		{
			var sequence = Interlocked.Increment(ref sequenceNumber);
			try
			{
				if (!Send(new StreamMessage
				{
					SubscriptionId = SubscriptionId,
					StreamKind = StreamKind,
					SequenceNumber = sequence,
					Data = Capture(sequence),
				}))
				{
					Stop();
					return;
				}
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				Send(new StreamMessage
				{
					SubscriptionId = SubscriptionId,
					StreamKind = StreamKind,
					SequenceNumber = sequence,
					Error = new CliStreamError
					{
						Code = ProtocolConstants.ErrorCodes.ProtocolError,
						Message = ex.Message,
					},
				});
				Stop();
				return;
			}

			await Task.Delay(IntervalMs, cancellation.Token).ContinueWith(static _ => { }, TaskScheduler.Default).ConfigureAwait(false);
		}
	}
}

public sealed class DelegateStreamSubscription : StreamSubscription
{
	private readonly Func<long, object> capture;

	public DelegateStreamSubscription(
		string subscriptionId,
		string streamKind,
		string? connectionId,
		int intervalMs,
		Func<object, bool> send,
		Func<long, object> capture)
		: base(subscriptionId, streamKind, connectionId, intervalMs, send)
	{
		this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
	}

	protected override object Capture(long sequenceNumber) => capture(sequenceNumber);
}
