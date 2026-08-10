namespace DeepFlowTest.Interop;

using System;
using System.IO;
using System.IO.Pipes;
using DeepFlowTest.Contracts;

public sealed class NamedPipeServer : IDisposable
{
	private readonly NamedPipeServerStream pipe;

	public NamedPipeServer(string pipeName)
	{
		PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
		pipe = CreatePipe(pipeName);
	}

	public string PipeName { get; }

	public void Dispose()
	{
		pipe.Dispose();
	}

	public Command WaitForNextCommand()
	{
		if (!pipe.IsConnected)
			pipe.WaitForConnection();

		var clientCommandFrame = MessagePacker.ReadFrame(pipe);
		if (!clientCommandFrame.HasFrame || clientCommandFrame.Message is null)
			throw new ProtocolException(ProtocolConstants.ErrorCodes.MalformedFrame, "Failed to read a command frame from the pipe.");

		var hasResponded = false;
		bool CheckHasResponded() => hasResponded;
		void Respond(object response)
		{
			if (hasResponded)
				return;

			MessagePacker.WriteFrame(pipe, response);
			hasResponded = true;
		}

		return new Command
		{
			Value = clientCommandFrame.Message,
			Respond = Respond,
			CheckHasResponded = CheckHasResponded,
			TrySend = response =>
			{
				if (hasResponded)
					return false;

				Respond(response);
				return true;
			},
		};
	}

	private static NamedPipeServerStream CreatePipe(string pipeName)
	{
		return new NamedPipeServerStream(
			pipeName,
			PipeDirection.InOut,
			maxNumberOfServerInstances: 1,
			transmissionMode: PipeTransmissionMode.Byte,
			options: PipeOptions.Asynchronous);
	}

	public struct Command
	{
		public object Value { get; set; }

		public Action<object> Respond { get; set; }

		public Func<bool> CheckHasResponded { get; set; }

		public string? ConnectionId { get; set; }

		public Action? HoldConnectionOpen { get; set; }

		public Func<object, bool>? TrySend { get; set; }
	}
}
