namespace DeepFlowTest;

using System;
using System.Threading;
using DeepFlowTest.Contracts;

/// <summary>Direct wire-protocol access that bypasses the typed AppDriver API.</summary>
public interface IUnsafeAppDriverCommandSession
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
