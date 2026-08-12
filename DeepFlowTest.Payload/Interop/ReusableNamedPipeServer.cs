namespace DeepFlowTest.Interop;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using DeepFlowTest.Contracts;

public sealed class ReusableNamedPipeServer : IDisposable
{
	private readonly object sync = new();
	private readonly List<ConnectionState> activeConnections = new();
	private readonly BlockingCollection<CommandQueueItem> commandQueue = new();
	private Thread? acceptThread;
	private int started;
	private volatile bool isDisposed;

	public ReusableNamedPipeServer(string pipeName)
	{
		PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
	}

	public event Action<string>? ClientDisconnected;

	public string PipeName { get; }

	public int ReceivedCommandCount => Volatile.Read(ref receivedCommandCount);

	public int DisconnectedClientCount => Volatile.Read(ref disconnectedClientCount);

	public int ActiveConnectionCount
	{
		get
		{
			lock (sync)
				return activeConnections.Count(connection => connection.Pipe.IsConnected);
		}
	}

	public void Dispose()
	{
		lock (sync)
		{
			if (isDisposed)
				return;

			isDisposed = true;
		}
		commandQueue.CompleteAdding();
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
		EnsureStarted();
		try
		{
			return commandQueue.Take().Command;
		}
		catch (InvalidOperationException) when (isDisposed)
		{
			throw new ObjectDisposedException(nameof(ReusableNamedPipeServer));
		}
	}

	private void EnsureStarted()
	{
		if (isDisposed)
			throw new ObjectDisposedException(nameof(ReusableNamedPipeServer));
		if (Interlocked.Exchange(ref started, 1) == 1)
			return;

		acceptThread = new Thread(AcceptConnections)
		{
			IsBackground = true,
			Name = $"{nameof(ReusableNamedPipeServer)}:{PipeName}",
		};
		acceptThread.Start();
	}

	private void AcceptConnections()
	{
		while (!isDisposed)
		{
			NamedPipeServerStream? pipe = null;
			try
			{
				pipe = new NamedPipeServerStream(
					PipeName,
					PipeDirection.InOut,
					maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
					transmissionMode: PipeTransmissionMode.Byte,
					options: PipeOptions.Asynchronous);
				var connection = new ConnectionState(pipe, Guid.NewGuid().ToString("N"), new StreamWriteLock());
				lock (sync)
					activeConnections.Add(connection);

				pipe.WaitForConnection();
				if (isDisposed)
				{
					ClosePipe(connection, clientDisconnected: true);
					return;
				}

				ThreadPool.QueueUserWorkItem(_ => ReadCommands(connection));
				pipe = null;
			}
			catch (ObjectDisposedException) when (isDisposed)
			{
				return;
			}
			catch (IOException) when (isDisposed)
			{
				return;
			}
			finally
			{
				pipe?.Dispose();
			}
		}
	}

	private void ReadCommands(ConnectionState connection)
	{
		while (!isDisposed && IsActive(connection))
		{
			MessagePacker.MessageFrame frame;
			try
			{
				frame = MessagePacker.ReadFrame(connection.Pipe);
			}
			catch (IOException)
			{
				HandleDisconnectedClient(connection);
				return;
			}
			catch (ObjectDisposedException)
			{
				HandleDisconnectedClient(connection);
				return;
			}

			if (!frame.HasFrame || frame.Message is null)
			{
				HandleDisconnectedClient(connection);
				return;
			}

			Interlocked.Increment(ref receivedCommandCount);
			Interlocked.Increment(ref connection.ReceivedCommandCount);
			var command = CreateCommand(connection, frame.Message);
			try
			{
				commandQueue.Add(new CommandQueueItem(command));
			}
			catch (InvalidOperationException) when (isDisposed)
			{
				return;
			}
		}
	}

	private NamedPipeServer.Command CreateCommand(ConnectionState connection, object value)
	{
		var hasResponded = 0;
		var connectionId = connection.ConnectionId;

		bool CheckHasResponded() => Volatile.Read(ref hasResponded) == 1;
		void HoldConnectionOpen() { }
		void Respond(object response)
		{
			if (Interlocked.Exchange(ref hasResponded, 1) == 1)
				return;

			TrySend(response);
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
			Value = value,
			Respond = Respond,
			CheckHasResponded = CheckHasResponded,
			ConnectionId = connectionId,
			HoldConnectionOpen = HoldConnectionOpen,
			TrySend = TrySend,
		};
	}

	private void HandleDisconnectedClient(ConnectionState connection)
	{
		if (ClosePipe(connection, clientDisconnected: true))
		{
			Interlocked.Increment(ref disconnectedClientCount);
			if (Volatile.Read(ref connection.ReceivedCommandCount) == 0 && !isDisposed && !commandQueue.IsAddingCompleted)
			{
				try
				{
					commandQueue.Add(new CommandQueueItem(command: null));
				}
				catch (InvalidOperationException) when (isDisposed)
				{
				}
			}
		}
	}

	private bool IsActive(ConnectionState connection)
	{
		lock (sync)
			return activeConnections.Contains(connection);
	}

	private bool ClosePipe(ConnectionState connection, bool clientDisconnected)
	{
		var removed = false;
		lock (sync)
			removed = activeConnections.Remove(connection);
		if (!removed)
			return false;

		connection.Pipe.Dispose();
		if (clientDisconnected && !string.IsNullOrEmpty(connection.ConnectionId))
			ClientDisconnected?.Invoke(connection.ConnectionId);
		return true;
	}

	private int receivedCommandCount;
	private int disconnectedClientCount;

	private sealed class CommandQueueItem
	{
		public CommandQueueItem(NamedPipeServer.Command? command)
		{
			Command = command;
		}

		public NamedPipeServer.Command? Command { get; }
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

		public int ReceivedCommandCount;
	}
}
