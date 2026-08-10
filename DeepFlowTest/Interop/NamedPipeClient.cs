namespace DeepFlowTest.Interop;

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;

internal sealed class NamedPipeClient : IDisposable
{
	private readonly SemaphoreSlim sendLock = new(1, 1);
	private readonly Func<int?> getTargetExitCode;
	private readonly Func<string?> readTargetCrashLog;
	private readonly Action? requestReinjection;
	private readonly int connectTimeoutMs;
	private readonly int connectRetryCount;
	private NamedPipeClientStream pipe;
	private bool isDisposed;

	public NamedPipeClient(
		string pipeName,
		Func<int?>? getTargetExitCode = null,
		Func<string?>? readTargetCrashLog = null,
		Action? requestReinjection = null,
		int connectTimeoutMs = TimeoutDefaults.NamedPipeConnectTimeoutMs,
		int connectRetryCount = TimeoutDefaults.NamedPipeConnectRetryCount)
	{
		PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
		this.getTargetExitCode = getTargetExitCode ?? (() => null);
		this.readTargetCrashLog = readTargetCrashLog ?? (() => null);
		this.requestReinjection = requestReinjection;
		this.connectTimeoutMs = connectTimeoutMs;
		this.connectRetryCount = Math.Max(1, connectRetryCount);
		pipe = CreatePipe();
	}

	public string PipeName { get; }

	public void Dispose()
	{
		sendLock.Wait();
		try
		{
			if (isDisposed)
				return;

			isDisposed = true;
			pipe.Dispose();
		}
		finally
		{
			sendLock.Release();
		}
	}

	public object Send(object command, int responseTimeoutMs = TimeoutDefaults.CommandTimeoutMs)
	{
		return SendAsync(command, responseTimeoutMs).ConfigureAwait(false).GetAwaiter().GetResult();
	}

	public async Task<object> SendAsync(object command, int responseTimeoutMs = TimeoutDefaults.CommandTimeoutMs, CancellationToken cancellationToken = default)
	{
		_ = command ?? throw new ArgumentNullException(nameof(command));
		if (responseTimeoutMs <= 0)
			throw new ArgumentOutOfRangeException(nameof(responseTimeoutMs), responseTimeoutMs, "The response timeout must be greater than zero.");

		await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfDisposed();
			ThrowIfTargetExited();
			await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
			ThrowIfTargetExited();

			using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(responseTimeoutMs));
			var operationToken = timeoutSource.Token;

			try
			{
				await MessagePacker.WriteFrameAsync(pipe, command, operationToken).ConfigureAwait(false);
				ThrowIfTargetExited();

				var responseFrame = await MessagePacker.ReadFrameAsync(pipe, operationToken).ConfigureAwait(false);
				if (!responseFrame.HasFrame || responseFrame.Message is null)
				{
					ResetPipe();
					throw new NamedPipeSessionException(ProtocolConstants.ErrorCodes.ProtocolError, "The pipe closed before a response frame was received.");
				}

				return responseFrame.Message;
			}
			catch (OperationCanceledException ex)
			{
				ResetPipe();
				ThrowIfTargetExited();
				if (cancellationToken.IsCancellationRequested)
					throw;

				throw new NamedPipeSessionException(ProtocolConstants.ErrorCodes.CommandTimeout, $"Command timed out after {responseTimeoutMs} ms.", ex);
			}
			catch (IOException ex)
			{
				ResetPipe();
				ThrowIfTargetExited();
				throw new NamedPipeSessionException(ProtocolConstants.ErrorCodes.ProtocolError, "The pipe disconnected while processing a command.", ex);
			}
			catch (ProtocolException ex)
			{
				ResetPipe();
				throw new NamedPipeSessionException(ex.ErrorCode, "The pipe returned a malformed response.", ex);
			}
		}
		finally
		{
			sendLock.Release();
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
				ThrowIfTargetExited();
			}
		}

		ThrowIfTargetExited();
		throw new NamedPipeSessionException(ProtocolConstants.ErrorCodes.ProtocolError, $"Could not connect to pipe '{PipeName}'.", lastTimeout!);
	}

	private NamedPipeClientStream CreatePipe()
	{
		return new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
	}

	private void ResetPipe()
	{
		pipe.Dispose();
		if (!isDisposed)
			pipe = CreatePipe();
	}

	private void ThrowIfDisposed()
	{
		if (isDisposed)
			throw new ObjectDisposedException(nameof(NamedPipeClient));
	}

	private void ThrowIfTargetExited()
	{
		var exitCode = getTargetExitCode();
		if (!exitCode.HasValue)
			return;

		var crashLog = TryReadTargetCrashLog();
		var message = $"Target process exited with code {exitCode.Value}.";
		if (!string.IsNullOrWhiteSpace(crashLog))
			message += $"{Environment.NewLine}Last unhandled exception:{Environment.NewLine}{crashLog}";

		throw new NamedPipeSessionException(
			ProtocolConstants.ErrorCodes.TargetExited,
			message,
			targetExitCode: exitCode.Value,
			crashLog: crashLog);
	}

	private string? TryReadTargetCrashLog()
	{
		try
		{
			var crashLog = readTargetCrashLog();
			return string.IsNullOrWhiteSpace(crashLog) ? null : crashLog;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			return null;
		}
	}

}
