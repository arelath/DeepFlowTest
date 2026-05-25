namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.Extensions.Options;

internal sealed class McpStreamRegistry : IDisposable
{
	private readonly object gate = new();
	private readonly IOptions<McpServerOptions> options;
	private readonly Dictionary<string, StreamState> streams = new(StringComparer.Ordinal);

	public McpStreamRegistry(IOptions<McpServerOptions> options)
	{
		this.options = options ?? throw new ArgumentNullException(nameof(options));
	}

	public StreamStartResult Start(
		McpSession session,
		StartSendingCommandRequest request,
		int timeoutMs)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(request);

		var stream = session.AppSession.StartStream(request, timeoutMs);
		var state = new StreamState(session.SessionId, session.AppSession, stream, options.Value.StreamBufferSize, timeoutMs);
		lock (gate)
			streams[state.StreamId] = state;

		state.Start();
		return new StreamStartResult
		{
			StreamId = state.StreamId,
			SubscriptionId = stream.Start.SubscriptionId,
			Start = stream.Start,
		};
	}

	public StreamReadResult Read(string streamId, int maxFrames)
	{
		var state = Get(streamId);
		return state.Read(Math.Max(1, maxFrames));
	}

	public StreamStopResult Stop(string streamId)
	{
		StreamState state;
		lock (gate)
		{
			if (!streams.Remove(streamId, out state!))
				throw new CliException(CliErrorCodes.InvalidArguments, $"Stream '{streamId}' was not found.");
		}

		state.Dispose();
		return new StreamStopResult
		{
			StreamId = state.StreamId,
			SubscriptionId = state.SubscriptionId,
			DroppedFrames = state.DroppedFrames,
		};
	}

	public void StopAll()
	{
		StreamState[] current;
		lock (gate)
		{
			current = [.. streams.Values];
			streams.Clear();
		}

		foreach (var stream in current)
			stream.Dispose();
	}

	public void Dispose()
	{
		StopAll();
	}

	private StreamState Get(string streamId)
	{
		lock (gate)
		{
			if (streams.TryGetValue(streamId, out var state))
				return state;
		}

		throw new CliException(CliErrorCodes.InvalidArguments, $"Stream '{streamId}' was not found.");
	}

	private sealed class StreamState : IDisposable
	{
		private readonly object gate = new();
		private readonly ICliAppSession appSession;
		private readonly ICliStreamSession stream;
		private readonly McpSemanticRecordingFormatter? semanticRecordingFormatter;
		private readonly int capacity;
		private readonly int timeoutMs;
		private readonly Queue<StreamMessage> frames = new();
		private readonly CancellationTokenSource cancellation = new();
		private Task? readTask;
		private bool disposed;

		public StreamState(Guid sessionId, ICliAppSession appSession, ICliStreamSession stream, int capacity, int timeoutMs)
		{
			SessionId = sessionId;
			this.appSession = appSession;
			this.stream = stream;
			semanticRecordingFormatter = string.Equals(stream.Start.StreamKind, ProtocolConstants.StreamKinds.SemanticRecording, StringComparison.Ordinal)
				? new McpSemanticRecordingFormatter()
				: null;
			this.capacity = Math.Max(1, capacity);
			this.timeoutMs = Math.Max(1, timeoutMs);
		}

		public Guid SessionId { get; }

		public string StreamId { get; } = Guid.NewGuid().ToString("N");

		public string SubscriptionId => stream.Start.SubscriptionId;

		public int DroppedFrames { get; private set; }

		public void Start()
		{
			readTask = Task.Run(ReadLoop);
		}

		public StreamReadResult Read(int maxFrames)
		{
			List<StreamMessage> result = [];
			lock (gate)
			{
				while (frames.Count != 0 && result.Count < maxFrames)
					result.Add(frames.Dequeue());
			}

			return new StreamReadResult
			{
				StreamId = StreamId,
				SubscriptionId = SubscriptionId,
				Frames = semanticRecordingFormatter is null ? result : [],
				FrameCount = result.Count,
				Recording = semanticRecordingFormatter?.FormatStreamMessages(result),
				DroppedFrames = DroppedFrames,
			};
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			cancellation.Cancel();
			try
			{
				readTask?.Wait(500);
			}
			catch (AggregateException)
			{
			}
			finally
			{
				try
				{
					appSession.Send<StopSendingCommandResponse>(
						new StopSendingCommandRequest(SubscriptionId, Math.Min(timeoutMs, TimeoutDefaults.StreamStopTimeoutMs)),
						Math.Min(timeoutMs, TimeoutDefaults.StreamStopTimeoutMs));
				}
				catch (CliException)
				{
				}

				stream.Dispose();
				semanticRecordingFormatter?.Dispose();
				cancellation.Dispose();
			}
		}

		private void ReadLoop()
		{
			while (!cancellation.IsCancellationRequested)
			{
				StreamMessage? frame;
				try
				{
					frame = stream.ReadFrame(TimeoutDefaults.StreamIntervalMs, cancellation.Token);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (CliException)
				{
					break;
				}

				if (frame is null)
				{
					Thread.Sleep(Math.Min(TimeoutDefaults.StreamIntervalMs, 50));
					continue;
				}

				lock (gate)
				{
					if (frames.Count >= capacity)
					{
						frames.Dequeue();
						DroppedFrames++;
					}

					frames.Enqueue(frame);
				}
			}
		}
	}
}

internal sealed record class StreamStartResult
{
	public string StreamId { get; init; } = string.Empty;

	public string SubscriptionId { get; init; } = string.Empty;

	public StartSendingCommandResponse Start { get; init; } = new();
}

internal sealed record class StreamReadResult
{
	public string StreamId { get; init; } = string.Empty;

	public string SubscriptionId { get; init; } = string.Empty;

	public IReadOnlyList<StreamMessage> Frames { get; init; } = [];

	public int FrameCount { get; init; }

	public McpCondensedRecordingOutput? Recording { get; init; }

	public int DroppedFrames { get; init; }
}

internal sealed record class StreamStopResult
{
	public string StreamId { get; init; } = string.Empty;

	public string SubscriptionId { get; init; } = string.Empty;

	public int DroppedFrames { get; init; }
}
