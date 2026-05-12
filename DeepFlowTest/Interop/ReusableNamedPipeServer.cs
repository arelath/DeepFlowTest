namespace DeepFlowTest.Interop;

using System;
using System.IO;
using System.IO.Pipes;
using DeepFlowTest.Contracts;

public sealed class ReusableNamedPipeServer : IDisposable
{
	private NamedPipeServerStream? activePipe;
	private StreamWriteLock? activeWriteLock;
	private string? activeConnectionId;
	private bool isDisposed;

	public ReusableNamedPipeServer(string pipeName)
	{
		PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
	}

	public event Action<string>? ClientDisconnected;

	public string PipeName { get; }

	public int ReceivedCommandCount { get; private set; }

	public int DisconnectedClientCount { get; private set; }

	public void Dispose()
	{
		isDisposed = true;
		CloseActivePipe(clientDisconnected: true);
	}

	public NamedPipeServer.Command? WaitForNextCommand()
	{
		if (isDisposed)
			throw new ObjectDisposedException(nameof(ReusableNamedPipeServer));

		var pipe = GetConnectedPipe();
		MessagePacker.MessageFrame frame;
		try
		{
			frame = MessagePacker.ReadFrame(pipe);
		}
		catch (IOException)
		{
			HandleDisconnectedClient(pipe);
			return null;
		}
		catch (ObjectDisposedException)
		{
			HandleDisconnectedClient(pipe);
			return null;
		}

		if (!frame.HasFrame || frame.Message is null)
		{
			HandleDisconnectedClient(pipe);
			return null;
		}

		ReceivedCommandCount++;
		var hasResponded = false;
		var keepConnectionOpen = false;
		var connectionId = activeConnectionId;

		bool CheckHasResponded() => hasResponded;
		void HoldConnectionOpen() => keepConnectionOpen = true;
		void Respond(object response)
		{
			if (hasResponded)
				return;

			TrySend(response);
			hasResponded = true;
			if (!keepConnectionOpen)
				ClosePipe(pipe, clientDisconnected: false);
		}

		bool TrySend(object response)
		{
			var writeLock = activeWriteLock;
			if (writeLock is null || !ReferenceEquals(activePipe, pipe))
				return false;

			try
			{
				return writeLock.TryWrite(pipe, response);
			}
			catch (IOException)
			{
				HandleDisconnectedClient(pipe);
				return false;
			}
			catch (ObjectDisposedException)
			{
				HandleDisconnectedClient(pipe);
				return false;
			}
		}

		return new NamedPipeServer.Command
		{
			Value = frame.Message,
			Respond = Respond,
			CheckHasResponded = CheckHasResponded,
			ConnectionId = connectionId,
			HoldConnectionOpen = HoldConnectionOpen,
			TrySend = TrySend,
		};
	}

	private NamedPipeServerStream GetConnectedPipe()
	{
		if (activePipe is not null && activePipe.IsConnected)
			return activePipe;

		var pipe = new NamedPipeServerStream(
			PipeName,
			PipeDirection.InOut,
			maxNumberOfServerInstances: 1,
			transmissionMode: PipeTransmissionMode.Byte,
			options: PipeOptions.Asynchronous);
		activePipe = pipe;
		activeWriteLock = new StreamWriteLock();
		activeConnectionId = Guid.NewGuid().ToString("N");
		pipe.WaitForConnection();
		return pipe;
	}

	private void HandleDisconnectedClient(NamedPipeServerStream pipe)
	{
		DisconnectedClientCount++;
		ClosePipe(pipe, clientDisconnected: true);
	}

	private void CloseActivePipe(bool clientDisconnected)
	{
		if (activePipe is not null)
			ClosePipe(activePipe, clientDisconnected);
	}

	private void ClosePipe(NamedPipeServerStream pipe, bool clientDisconnected)
	{
		var connectionId = activeConnectionId;
		if (ReferenceEquals(activePipe, pipe))
		{
			activePipe = null;
			activeWriteLock = null;
			activeConnectionId = null;
		}

		pipe.Dispose();
		if (clientDisconnected && !string.IsNullOrEmpty(connectionId))
			ClientDisconnected?.Invoke(connectionId!);
	}
}
