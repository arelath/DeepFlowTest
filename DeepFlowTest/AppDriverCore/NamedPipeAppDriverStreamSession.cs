namespace DeepFlowTest;

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal sealed class NamedPipeAppDriverStreamSession : IAppDriverStreamSession
{
	private readonly NamedPipeClientStream pipe;
	private readonly Func<int?> getTargetExitCode;

	private NamedPipeAppDriverStreamSession(NamedPipeClientStream pipe, Func<int?> getTargetExitCode, StartSendingCommandResponse start)
	{
		this.pipe = pipe;
		this.getTargetExitCode = getTargetExitCode;
		Start = start;
	}

	public StartSendingCommandResponse Start { get; }

	public static NamedPipeAppDriverStreamSession Create(
		string pipeName,
		StartSendingCommandRequest command,
		Func<int?> getTargetExitCode,
		int timeoutMs)
	{
		var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		try
		{
			pipe.Connect(Math.Max(1, timeoutMs));
			ThrowIfTargetExited(getTargetExitCode);
			MessagePacker.WriteFrame(pipe, command);
			var response = TimeoutAfter(MessagePacker.ReadFrameAsync(pipe), timeoutMs, CancellationToken.None)
				.ConfigureAwait(false)
				.GetAwaiter()
				.GetResult();
			if (!response.HasFrame || response.Message is null)
				throw new AppDriverException(ProtocolConstants.ErrorCodes.ProtocolError, "The stream pipe closed before the start response was received.");
			if (response.Message is StandardIpcResponse standard && standard.Success == false)
				DriverCommandClient.ThrowIfStandardFailure(standard, "Stream command failed.");

			return new NamedPipeAppDriverStreamSession(
				pipe,
				getTargetExitCode,
				MessagePacker.ConvertTo<StartSendingCommandResponse>(response.Message));
		}
		catch
		{
			pipe.Dispose();
			throw;
		}
	}

	public StreamMessage? ReadFrame(int timeoutMs, CancellationToken cancellationToken = default)
	{
		ThrowIfTargetExited(getTargetExitCode);
		try
		{
			var frame = TimeoutAfter(MessagePacker.ReadFrameAsync(pipe, cancellationToken), timeoutMs, cancellationToken)
				.ConfigureAwait(false)
				.GetAwaiter()
				.GetResult();
			if (!frame.HasFrame || frame.Message is null)
				return null;
			if (frame.Message is StandardIpcResponse standard && standard.Success == false)
				DriverCommandClient.ThrowIfStandardFailure(standard, "Stream command failed.");

			return MessagePacker.ConvertTo<StreamMessage>(frame.Message);
		}
		catch (TimeoutException)
		{
			return null;
		}
		catch (ProtocolException ex)
		{
			throw new AppDriverException(ex.ErrorCode, ex.Message, ex);
		}
		catch (IOException ex)
		{
			throw new AppDriverException(ProtocolConstants.ErrorCodes.ProtocolError, ex.Message, ex);
		}
	}

	public void Dispose()
	{
		pipe.Dispose();
	}

	private static void ThrowIfTargetExited(Func<int?> getTargetExitCode)
	{
		var exitCode = getTargetExitCode();
		if (exitCode.HasValue)
			throw new AppDriverException(ProtocolConstants.ErrorCodes.TargetExited, $"Target process exited with code {exitCode.Value}.");
	}

	private static async Task<T> TimeoutAfter<T>(Task<T> task, int timeoutMs, CancellationToken cancellationToken)
	{
		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var completed = await Task.WhenAny(task, Task.Delay(Math.Max(1, timeoutMs), timeoutSource.Token)).ConfigureAwait(false);
		if (completed == task)
		{
			timeoutSource.Cancel();
			return await task.ConfigureAwait(false);
		}

		throw new TimeoutException();
	}
}
