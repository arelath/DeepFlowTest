namespace DeepFlowTest.Interop;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using DeepFlowTest.Contracts;

public sealed class ReusableNamedPipeServer : IDisposable
{
	private readonly object sync = new();
	private readonly List<ConnectionState> activeConnections = new();
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
		ConnectionState[] connections;
		lock (sync)
			connections = activeConnections.ToArray();

		foreach (var connection in connections)
			ClosePipe(connection, clientDisconnected: true);
	}

	public void CloseConnection(string connectionId)
	{
		if (string.IsNullOrWhiteSpace(connectionId))
			return;

		ConnectionState? connection;
		lock (sync)
			connection = activeConnections.FirstOrDefault(item => string.Equals(item.ConnectionId, connectionId, StringComparison.Ordinal));

		if (connection is not null)
			ClosePipe(connection, clientDisconnected: false);
	}

	public NamedPipeServer.Command? WaitForNextCommand()
	{
		if (isDisposed)
			throw new ObjectDisposedException(nameof(ReusableNamedPipeServer));

		var connection = GetConnectedPipe();
		MessagePacker.MessageFrame frame;
		try
		{
			frame = MessagePacker.ReadFrame(connection.Pipe);
		}
		catch (IOException)
		{
			HandleDisconnectedClient(connection);
			return null;
		}
		catch (ObjectDisposedException)
		{
			HandleDisconnectedClient(connection);
			return null;
		}

		if (!frame.HasFrame || frame.Message is null)
		{
			HandleDisconnectedClient(connection);
			return null;
		}

		ReceivedCommandCount++;
		var hasResponded = false;
		var keepConnectionOpen = false;
		var connectionId = connection.ConnectionId;

		bool CheckHasResponded() => hasResponded;
		void HoldConnectionOpen() => keepConnectionOpen = true;
		void Respond(object response)
		{
			if (hasResponded)
				return;

			TrySend(response);
			hasResponded = true;
			if (!keepConnectionOpen)
				ClosePipe(connection, clientDisconnected: false);
		}

		bool TrySend(object response)
		{
			if (!IsActive(connection))
				return false;

			try
			{
				return connection.WriteLock.TryWrite(connection.Pipe, response);
			}
			catch (IOException)
			{
				HandleDisconnectedClient(connection);
				return false;
			}
			catch (ObjectDisposedException)
			{
				HandleDisconnectedClient(connection);
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

	private ConnectionState GetConnectedPipe()
	{
		var pipe = new NamedPipeServerStream(
			PipeName,
			PipeDirection.InOut,
			maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
			transmissionMode: PipeTransmissionMode.Byte,
			options: PipeOptions.Asynchronous);
		var connection = new ConnectionState(pipe, Guid.NewGuid().ToString("N"), new StreamWriteLock());
		lock (sync)
			activeConnections.Add(connection);

		pipe.WaitForConnection();
		return connection;
	}

	private void HandleDisconnectedClient(ConnectionState connection)
	{
		DisconnectedClientCount++;
		ClosePipe(connection, clientDisconnected: true);
	}

	private bool IsActive(ConnectionState connection)
	{
		lock (sync)
			return activeConnections.Contains(connection);
	}

	private void ClosePipe(ConnectionState connection, bool clientDisconnected)
	{
		lock (sync)
			activeConnections.Remove(connection);

		connection.Pipe.Dispose();
		if (clientDisconnected && !string.IsNullOrEmpty(connection.ConnectionId))
			ClientDisconnected?.Invoke(connection.ConnectionId);
	}

	private sealed class ConnectionState
	{
		public ConnectionState(NamedPipeServerStream pipe, string connectionId, StreamWriteLock writeLock)
		{
			Pipe = pipe;
			ConnectionId = connectionId;
			WriteLock = writeLock;
		}

		public NamedPipeServerStream Pipe { get; }

		public string ConnectionId { get; }

		public StreamWriteLock WriteLock { get; }
	}
}
