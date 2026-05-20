namespace DeepFlowTest;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

public sealed class SemanticRecordingSession : IDisposable
{
	private static readonly JsonSerializerSettings JsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		NullValueHandling = NullValueHandling.Ignore,
		TypeNameHandling = TypeNameHandling.None,
	};

	private readonly IAppDriverCommandSession commandSession;
	private readonly IAppDriverStreamSession streamSession;
	private readonly CancellationTokenSource cancellation = new();
	private readonly StreamWriter writer;
	private readonly int timeoutMs;
	private readonly bool compactOutput;
	private readonly CompactSemanticRecordingState? compactState;
	private readonly Task readerTask;
	private long framesWritten;
	private int droppedActionCount;
	private bool wroteFrame;
	private Exception? backgroundError;
	private bool disposed;

	internal SemanticRecordingSession(
		IAppDriverCommandSession commandSession,
		IAppDriverStreamSession streamSession,
		string outputPath,
		int timeoutMs,
		bool compactOutput)
	{
		this.commandSession = commandSession ?? throw new ArgumentNullException(nameof(commandSession));
		this.streamSession = streamSession ?? throw new ArgumentNullException(nameof(streamSession));
		OutputPath = NormalizeOutputPath(outputPath);
		this.timeoutMs = Math.Max(1, timeoutMs);
		this.compactOutput = compactOutput;
		compactState = compactOutput ? new CompactSemanticRecordingState() : null;
		writer = new StreamWriter(new FileStream(OutputPath, FileMode.Create, FileAccess.Write, FileShare.Read));
		writer.WriteLine("[");
		writer.Flush();
		readerTask = Task.Run(ReadLoop);
	}

	public string OutputPath { get; }

	public long FramesWritten => Interlocked.Read(ref framesWritten);

	public int DroppedActionCount => Volatile.Read(ref droppedActionCount);

	internal static SemanticRecordingSession Start(
		IAppDriverCommandSession commandSession,
		string outputPath,
		SemanticRecordingOptions options,
		int defaultTimeoutMs)
	{
		_ = commandSession ?? throw new ArgumentNullException(nameof(commandSession));
		_ = options ?? throw new ArgumentNullException(nameof(options));
		if (commandSession is not IAppDriverStreamingSession streamingSession)
		{
			throw new AppDriverException(
				ProtocolConstants.ErrorCodes.UnsupportedProtocol,
				"The current AppDriver command session does not support streaming recording.");
		}

		var timeoutMs = Math.Max(1, options.TimeoutMs ?? defaultTimeoutMs);
		var request = new StartSendingCommandRequest
		{
			StreamKind = ProtocolConstants.StreamKinds.SemanticRecording,
			IntervalMs = options.IntervalMs,
			PropNames = options.PropNames?.ToArray(),
			TargetId = options.RootTargetId,
			TimeoutMs = timeoutMs,
			SemanticRecording = new SemanticRecordingOptionsDto
			{
				IncludeInitialSnapshot = options.IncludeInitialSnapshot,
				TextIdleMs = options.TextIdleMs,
				MaxQueuedActions = options.MaxQueuedActions,
				MaxBatchFrames = options.MaxBatchFrames,
				MaxNodeCount = options.MaxNodeCount,
			},
		};
		return new SemanticRecordingSession(
			commandSession,
			streamingSession.StartStream(request, timeoutMs),
			outputPath,
			timeoutMs,
			options.CompactOutput);
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		WaitForFramesBeforeStop(minFrames: 1);
		cancellation.Cancel();
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
			backgroundError ??= ex;
		}
		finally
		{
			streamSession.Dispose();
			try
			{
				readerTask.Wait(TimeoutDefaults.StreamCleanupTimeoutMs);
			}
			catch (AggregateException ex) when (ex.InnerExceptions.All(static inner => inner is OperationCanceledException or TaskCanceledException))
			{
			}

			CloseOutput();
			writer.Dispose();
			cancellation.Dispose();
		}

		if (backgroundError is not null)
			throw new AppDriverException(ProtocolConstants.ErrorCodes.ProtocolError, backgroundError.Message, backgroundError);
	}

	private void ReadLoop()
	{
		var readTimeout = Math.Max(timeoutMs, Math.Max(1, streamSession.Start.IntervalMs) * 2);
		while (!cancellation.IsCancellationRequested)
		{
			try
			{
				var frame = streamSession.ReadFrame(readTimeout, cancellation.Token);
				if (frame is null)
					continue;
				if (frame.Error is not null)
					throw new AppDriverException(frame.Error.Code, frame.Error.Message);
				if (frame.Data is null)
					continue;

				var batch = MessagePacker.ConvertTo<SemanticRecordingBatch>(frame.Data);
				Interlocked.Add(ref droppedActionCount, Math.Max(0, batch.DroppedActionCount));
				foreach (var recordingFrame in batch.Frames ?? [])
					WriteFrame(recordingFrame);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				backgroundError = ex;
				return;
			}
		}
	}

	private void WriteFrame(SemanticRecordingFrame frame)
	{
		lock (writer)
		{
			object output = compactOutput ? CompactSemanticRecordingFrame.Create(frame, compactState) : frame;
			if (wroteFrame)
				writer.WriteLine(",");

			writer.Write(JsonConvert.SerializeObject(output, Formatting.Indented, JsonSettings));
			writer.Flush();
			wroteFrame = true;
			Interlocked.Increment(ref framesWritten);
		}
	}

	private void CloseOutput()
	{
		lock (writer)
		{
			if (wroteFrame)
				writer.WriteLine();
			writer.WriteLine("]");
			writer.Flush();
		}
	}

	private void WaitForFramesBeforeStop(int minFrames)
	{
		if (FramesWritten >= minFrames || backgroundError is not null || readerTask.IsCompleted)
			return;

		var timeout = TimeSpan.FromMilliseconds(Math.Min(timeoutMs, TimeoutDefaults.StreamStopTimeoutMs));
		SpinWait.SpinUntil(
			() => FramesWritten >= minFrames || backgroundError is not null || readerTask.IsCompleted,
			timeout);
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
