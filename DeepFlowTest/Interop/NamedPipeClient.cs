namespace DeepFlowTest.Interop;

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;

public sealed class NamedPipeClient : IDisposable
{
	private readonly NamedPipeClientStream pipe;
	private readonly Func<int?> getTargetExitCode;
	private readonly Action? requestReinjection;
	private readonly int connectTimeoutMs;
	private readonly int connectRetryCount;

	public NamedPipeClient(
		string pipeName,
		Func<int?>? getTargetExitCode = null,
		Action? requestReinjection = null,
		int connectTimeoutMs = 5_000,
		int connectRetryCount = 2)
	{
		PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
		this.getTargetExitCode = getTargetExitCode ?? (() => null);
		this.requestReinjection = requestReinjection;
		this.connectTimeoutMs = connectTimeoutMs;
		this.connectRetryCount = Math.Max(1, connectRetryCount);
		pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
	}

	public string PipeName { get; }

	public void Dispose()
	{
		pipe.Dispose();
	}

	public object Send(object command, int responseTimeoutMs = 10_000)
	{
		return SendAsync(command, responseTimeoutMs).ConfigureAwait(false).GetAwaiter().GetResult();
	}

	public async Task<object> SendAsync(object command, int responseTimeoutMs = 10_000, CancellationToken cancellationToken = default)
	{
		_ = command ?? throw new ArgumentNullException(nameof(command));

		ThrowIfTargetExited();
		await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
		ThrowIfTargetExited();

		try
		{
			await TimeoutAfter(MessagePacker.WriteFrameAsync(pipe, command, cancellationToken), TimeSpan.FromMilliseconds(responseTimeoutMs), cancellationToken)
				.ConfigureAwait(false);
			ThrowIfTargetExited();

			var responseFrame = await TimeoutAfter(MessagePacker.ReadFrameAsync(pipe, cancellationToken), TimeSpan.FromMilliseconds(responseTimeoutMs), cancellationToken)
				.ConfigureAwait(false);
			if (!responseFrame.HasFrame || responseFrame.Message is null)
				throw new NamedPipeSessionException(ProtocolConstants.ErrorCodes.ProtocolError, "The pipe closed before a response frame was received.");

			return responseFrame.Message;
		}
		catch (TimeoutException ex)
		{
			ThrowIfTargetExited();
			throw new NamedPipeSessionException(ProtocolConstants.ErrorCodes.CommandTimeout, $"Command timed out after {responseTimeoutMs} ms.", ex);
		}
		catch (IOException ex)
		{
			ThrowIfTargetExited();
			throw new NamedPipeSessionException(ProtocolConstants.ErrorCodes.ProtocolError, "The pipe disconnected while processing a command.", ex);
		}
		catch (ProtocolException ex)
		{
			throw new NamedPipeSessionException(ex.ErrorCode, "The pipe returned a malformed response.", ex);
		}
	}

	private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
	{
		if (pipe.IsConnected)
			return;

		TimeoutException? lastTimeout = null;
		for (var attempt = 0; attempt < connectRetryCount; attempt++)
		{
			try
			{
				await Task.Run(() => pipe.Connect(connectTimeoutMs), cancellationToken).ConfigureAwait(false);
				return;
			}
			catch (TimeoutException ex)
			{
				lastTimeout = ex;
				requestReinjection?.Invoke();
			}
		}

		throw new NamedPipeSessionException(ProtocolConstants.ErrorCodes.ProtocolError, $"Could not connect to pipe '{PipeName}'.", lastTimeout!);
	}

	private void ThrowIfTargetExited()
	{
		var exitCode = getTargetExitCode();
		if (exitCode.HasValue)
			throw new NamedPipeSessionException(ProtocolConstants.ErrorCodes.TargetExited, $"Target process exited with code {exitCode.Value}.");
	}

	private static async Task TimeoutAfter(Task task, TimeSpan timeout, CancellationToken cancellationToken)
	{
		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var completed = await Task.WhenAny(task, Task.Delay(timeout, timeoutSource.Token)).ConfigureAwait(false);
		if (completed == task)
		{
			timeoutSource.Cancel();
			await task.ConfigureAwait(false);
			return;
		}

		throw new TimeoutException();
	}

	private static async Task<T> TimeoutAfter<T>(Task<T> task, TimeSpan timeout, CancellationToken cancellationToken)
	{
		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var completed = await Task.WhenAny(task, Task.Delay(timeout, timeoutSource.Token)).ConfigureAwait(false);
		if (completed == task)
		{
			timeoutSource.Cancel();
			return await task.ConfigureAwait(false);
		}

		throw new TimeoutException();
	}
}
