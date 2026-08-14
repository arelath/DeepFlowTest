namespace DeepFlowTest;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using Newtonsoft.Json;

public sealed class SemanticRecordingSession : IDisposable
{
	private readonly IUnsafeAppDriverCommandSession commandSession;
	private readonly IAppDriverStreamSession streamSession;
	private readonly CancellationTokenSource cancellation = new();
	private readonly StreamWriter? writer;
	private readonly FileStream? outputStream;
	private readonly ISemanticRecordingFrameWriter? frameWriter;
	private readonly SemanticRecordingBuffer? buffer;
	private readonly Action<SemanticRecordingBatch>? batchReceived;
	private readonly Action<Exception>? batchReceivedError;
	private readonly int timeoutMs;
	private readonly long maximumArtifactSizeBytes;
	private readonly SemanticRecordingOutputFormat outputFormat;
	private readonly ConcurrentQueue<AppDriverDiagnostic> diagnostics = new();
	private readonly Task readerTask;
	private long framesWritten;
	private int droppedActionCount;
	private Exception? backgroundError;
	private bool disposed;

	private SemanticRecordingSession(
		IUnsafeAppDriverCommandSession commandSession,
		IAppDriverStreamSession streamSession,
		string? outputPath,
		SemanticRecordingBuffer? buffer,
		int timeoutMs,
		SemanticRecordingOutputFormat outputFormat,
		long maximumArtifactSizeBytes,
		Action<SemanticRecordingBatch>? batchReceived,
		Action<Exception>? batchReceivedError)
	{
		this.commandSession = commandSession ?? throw new ArgumentNullException(nameof(commandSession));
		this.streamSession = streamSession ?? throw new ArgumentNullException(nameof(streamSession));
		this.buffer = buffer;
		this.timeoutMs = Math.Max(1, timeoutMs);
		this.maximumArtifactSizeBytes = maximumArtifactSizeBytes;
		this.outputFormat = outputFormat;
		this.batchReceived = batchReceived;
		this.batchReceivedError = batchReceivedError;
		if (outputPath is not null)
		{
			OutputPath = NormalizeOutputPath(outputPath);
			outputStream = new FileStream(OutputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
			writer = new StreamWriter(outputStream);
			frameWriter = SemanticRecordingFrameWriter.Create(writer, outputFormat);
			writer.Flush();
		}
		readerTask = Task.Run(ReadLoop);
	}

	public string? OutputPath { get; private set; }

	public long FramesWritten => Interlocked.Read(ref framesWritten);

	public int DroppedActionCount => Volatile.Read(ref droppedActionCount);

	public int DroppedBufferedFrameCount => buffer?.DroppedFrameCount ?? 0;

	public IReadOnlyList<AppDriverDiagnostic> Diagnostics => diagnostics.ToArray();

	internal static SemanticRecordingSession Start(
		IUnsafeAppDriverCommandSession commandSession,
		string outputPath,
		SemanticRecordingOptions options,
		int defaultTimeoutMs) =>
		StartCore(commandSession, outputPath, buffer: null, options, options.MaximumArtifactSizeBytes, defaultTimeoutMs);

	internal static SemanticRecordingSession StartBuffered(
		IUnsafeAppDriverCommandSession commandSession,
		SemanticRecordingOptions options,
		long bufferSizeBytes,
		int defaultTimeoutMs) =>
		StartCore(commandSession, outputPath: null, new SemanticRecordingBuffer(bufferSizeBytes), options, bufferSizeBytes, defaultTimeoutMs);

	private static SemanticRecordingSession StartCore(
		IUnsafeAppDriverCommandSession commandSession,
		string? outputPath,
		SemanticRecordingBuffer? buffer,
		SemanticRecordingOptions options,
		long maximumArtifactSizeBytes,
		int defaultTimeoutMs)
	{
		_ = commandSession ?? throw new ArgumentNullException(nameof(commandSession));
		_ = options ?? throw new ArgumentNullException(nameof(options));
		options.Validate();
		if (commandSession is not IAppDriverStreamingSession streamingSession)
		{
			throw new AppDriverException(
				ProtocolConstants.ErrorCodes.UnsupportedProtocol,
				"The current AppDriver command session does not support streaming recording.");
		}

		var timeoutMs = options.Timeout is TimeSpan timeout
			? DurationUtility.ToMilliseconds(timeout, nameof(options.Timeout))
			: Math.Max(1, defaultTimeoutMs);
		var request = new StartSendingCommandRequest
		{
			StreamKind = ProtocolConstants.StreamKinds.SemanticRecording,
			IntervalMs = DurationUtility.ToMilliseconds(options.Interval, nameof(options.Interval)),
			PropNames = options.PropNames?.ToArray(),
			TargetId = options.RootTargetId,
			TimeoutMs = timeoutMs,
			SemanticRecording = new SemanticRecordingOptionsDto
			{
				IncludeInitialSnapshot = options.IncludeInitialSnapshot,
				TextIdleMs = DurationUtility.ToMilliseconds(options.TextIdleDuration, nameof(options.TextIdleDuration), allowZero: true),
				MaxQueuedActions = options.MaxQueuedActions,
				MaxBatchFrames = options.MaxBatchFrames,
				MaxNodeCount = options.MaxNodeCount,
			},
		};
		return new SemanticRecordingSession(
			commandSession,
			streamingSession.StartStream(request, timeoutMs),
			outputPath,
			buffer,
			timeoutMs,
			options.OutputFormat,
			maximumArtifactSizeBytes,
			options.BatchReceived,
			options.BatchReceivedError);
	}

	public async Task CompleteAsync(CancellationToken cancellationToken = default)
	{
		await Task.Yield();
		cancellationToken.ThrowIfCancellationRequested();
		CompleteCore(throwOnError: true, requestRemoteStop: true);
	}

	public void Dispose() => CompleteCore(throwOnError: false, requestRemoteStop: true);

	internal void CompleteAfterTargetExit() => CompleteCore(throwOnError: false, requestRemoteStop: false);

	internal string? FlushBuffered(string outputPath, long maximumBytes)
	{
		if (buffer is null)
			return OutputPath;
		if (!disposed)
			CompleteCore(throwOnError: false, requestRemoteStop: true);

		try
		{
			var frames = buffer.Snapshot().ToList();
			string rendered;
			do
			{
				rendered = RenderFrames(frames, buffer.DroppedFrameCount + DroppedActionCount);
				if (Encoding.UTF8.GetByteCount(rendered) <= maximumBytes || frames.Count == 0)
					break;
				frames.RemoveAt(0);
			}
			while (true);

			if (Encoding.UTF8.GetByteCount(rendered) > maximumBytes)
			{
				AddDiagnostic(AppDriverDiagnosticSeverity.Warning, "artifact-size-limit", "The buffered semantic trace exceeded the per-test artifact limit and was not written.");
				return null;
			}

			OutputPath = NormalizeOutputPath(outputPath);
			File.WriteAllText(OutputPath, rendered, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			return OutputPath;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			SetBackgroundError(ex, "trace-flush-failed", "Failed to flush the buffered semantic trace.");
			return null;
		}
	}

	private string RenderFrames(IReadOnlyList<SemanticRecordingFrame> frames, int droppedFrames)
	{
		using var output = new StringWriter();
		using (var renderer = SemanticRecordingFrameWriter.Create(output, outputFormat))
		{
			if (droppedFrames > 0)
				renderer.WriteDroppedActionCount(droppedFrames);
			foreach (var frame in frames)
				renderer.WriteFrame(frame);
		}
		return output.ToString();
	}

	private void CompleteCore(bool throwOnError, bool requestRemoteStop)
	{
		if (!disposed)
		{
			disposed = true;
			WaitForFramesBeforeStop(minFrames: 1);
			cancellation.Cancel();
			if (requestRemoteStop)
			{
				try
				{
					if (!string.IsNullOrWhiteSpace(streamSession.Start.SubscriptionId))
					{
						commandSession.Send<StopSendingCommandResponse>(new StopSendingCommandRequest
						{
							SubscriptionId = streamSession.Start.SubscriptionId,
							TimeoutMs = Math.Min(timeoutMs, TimeoutDefaults.StreamStopTimeoutMs),
						});
					}
				}
				catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
				{
					SetBackgroundError(ex, "recording-stop-failed", "Failed to stop the semantic recording stream.");
				}
			}

			DisposeSafely(streamSession, "stream-dispose-failed", "Failed to dispose the semantic recording stream.");
			try
			{
				readerTask.Wait(TimeoutDefaults.StreamCleanupTimeoutMs);
			}
			catch (AggregateException ex) when (ex.InnerExceptions.All(static inner => inner is OperationCanceledException or TaskCanceledException))
			{
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				SetBackgroundError(ex, "recording-reader-cleanup-failed", "Failed while waiting for the semantic recording reader to stop.");
			}

			DisposeSafely(frameWriter, "trace-close-failed", "Failed to finalize the semantic trace.");
			DisposeSafely(writer, "trace-close-failed", "Failed to close the semantic trace.");
			DisposeSafely(outputStream, "trace-close-failed", "Failed to close the semantic trace stream.");
			DisposeSafely(cancellation, "recording-cleanup-failed", "Failed to clean up semantic recording cancellation state.");
		}

		if (throwOnError && backgroundError is not null)
			throw new AppDriverException(ProtocolConstants.ErrorCodes.ProtocolError, backgroundError.Message, backgroundError);
	}

	private void ReadLoop()
	{
		var readTimeout = Math.Max(timeoutMs, Math.Max(1, streamSession.Start.IntervalMs) * 2);
		while (!cancellation.IsCancellationRequested)
		{
			try
			{
				var streamFrame = streamSession.ReadFrame(readTimeout, cancellation.Token);
				if (streamFrame is null)
					continue;
				if (streamFrame.Error is not null)
					throw new AppDriverException(streamFrame.Error.Code, streamFrame.Error.Message);
				if (streamFrame.Data is null)
					continue;

				var batch = MessagePacker.ConvertTo<SemanticRecordingBatch>(streamFrame.Data);
				var recordingFrames = (batch.Frames ?? []).ToList();
				var receivedBatch = batch with { Frames = recordingFrames };
				Interlocked.Add(ref droppedActionCount, Math.Max(0, batch.DroppedActionCount));
				NotifyBatchReceived(receivedBatch);
				WriteDroppedActionCount(batch.DroppedActionCount);
				foreach (var recordingFrame in recordingFrames)
					WriteFrame(recordingFrame);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				SetBackgroundError(ex, "recording-reader-failed", "The semantic recording reader stopped unexpectedly.");
				return;
			}
		}
	}

	private void NotifyBatchReceived(SemanticRecordingBatch batch)
	{
		if (batchReceived is null)
			return;
		try
		{
			batchReceived(batch);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			try
			{
				batchReceivedError?.Invoke(ex);
			}
			catch (Exception callbackError) when (callbackError is not OutOfMemoryException && callbackError is not StackOverflowException)
			{
			}
		}
	}

	private void WriteFrame(SemanticRecordingFrame frame)
	{
		if (buffer is not null)
			buffer.Add(frame);
		else if (writer is not null && frameWriter is not null)
		{
			lock (writer)
			{
				frameWriter.WriteFrame(frame);
				writer.Flush();
				if (outputStream?.Length > maximumArtifactSizeBytes)
					throw new InvalidOperationException($"The semantic trace exceeded the {maximumArtifactSizeBytes} byte artifact limit.");
			}
		}
		Interlocked.Increment(ref framesWritten);
	}

	private void WriteDroppedActionCount(int count)
	{
		if (count <= 0 || writer is null || frameWriter is null)
			return;
		lock (writer)
			frameWriter.WriteDroppedActionCount(count);
	}

	private void WaitForFramesBeforeStop(int minFrames)
	{
		if (FramesWritten >= minFrames || backgroundError is not null || readerTask.IsCompleted)
			return;
		var timeout = TimeSpan.FromMilliseconds(Math.Min(timeoutMs, TimeoutDefaults.StreamStopTimeoutMs));
		SpinWait.SpinUntil(() => FramesWritten >= minFrames || backgroundError is not null || readerTask.IsCompleted, timeout);
	}

	private void SetBackgroundError(Exception ex, string code, string message)
	{
		backgroundError ??= ex;
		AddDiagnostic(AppDriverDiagnosticSeverity.Error, code, $"{message} {ex.Message}", ex);
	}

	private void AddDiagnostic(AppDriverDiagnosticSeverity severity, string code, string message, Exception? exception = null) =>
		diagnostics.Enqueue(new AppDriverDiagnostic { Severity = severity, Code = code, Message = message, Exception = exception });

	private void DisposeSafely(IDisposable? disposable, string code, string message)
	{
		try
		{
			disposable?.Dispose();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			SetBackgroundError(ex, code, message);
		}
	}

	private static string NormalizeOutputPath(string outputPath)
	{
		_ = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
		var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
		var directory = Path.GetDirectoryName(fullPath);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);
		return fullPath;
	}
}

internal sealed class SemanticRecordingBuffer
{
	private readonly object sync = new();
	private readonly Queue<(SemanticRecordingFrame Frame, int Size)> frames = new();
	private readonly long maximumBytes;
	private long currentBytes;

	public SemanticRecordingBuffer(long maximumBytes)
	{
		this.maximumBytes = maximumBytes;
	}

	public int DroppedFrameCount { get; private set; }

	public void Add(SemanticRecordingFrame frame)
	{
		var size = Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(frame));
		lock (sync)
		{
			if (size > maximumBytes)
			{
				DroppedFrameCount++;
				return;
			}
			while (frames.Count > 0 && currentBytes + size > maximumBytes)
			{
				currentBytes -= frames.Dequeue().Size;
				DroppedFrameCount++;
			}
			frames.Enqueue((frame, size));
			currentBytes += size;
		}
	}

	public IReadOnlyList<SemanticRecordingFrame> Snapshot()
	{
		lock (sync)
			return frames.Select(static item => item.Frame).ToArray();
	}
}
