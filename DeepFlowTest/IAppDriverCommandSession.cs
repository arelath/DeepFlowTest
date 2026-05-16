namespace DeepFlowTest;

using System;
using System.Threading;
using DeepFlowTest.Contracts;

public interface IAppDriverCommandSession
{
	TResponse Send<TResponse>(IpcCommand command);
}

internal interface IAppDriverStreamingSession
{
	IAppDriverStreamSession StartStream(StartSendingCommandRequest command, int timeoutMs);
}

internal interface IAppDriverStreamSession : IDisposable
{
	StartSendingCommandResponse Start { get; }

	StreamMessage? ReadFrame(int timeoutMs, CancellationToken cancellationToken = default);
}
