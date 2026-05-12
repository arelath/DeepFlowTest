namespace DeepFlowTest;

using DeepFlowTest.Contracts;

public interface IAppDriverCommandSession
{
	TResponse Send<TResponse>(IpcCommand command);
}
