namespace DeepFlowTest.Tests.Fakes;

using System.Collections.Generic;
using DeepFlowTest.Contracts;

internal sealed class FakeAppDriverCommandSession : IAppDriverCommandSession
{
	private readonly Queue<object> responses;

	public FakeAppDriverCommandSession(params object[] responses)
	{
		this.responses = new Queue<object>(responses);
	}

	public List<IpcCommand> SentCommands { get; } = [];

	public TResponse Send<TResponse>(IpcCommand command)
	{
		SentCommands.Add(command);
		return (TResponse)responses.Dequeue();
	}
}
