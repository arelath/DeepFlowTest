namespace DeepFlowTest.AppDriverPayload.Commands;

using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;

internal static class PingCommand
{
	public static void Process(PingCommandRequest request, NamedPipeServer.Command command)
	{
		var availability = ThreadUtility.GetAvailability();
		command.Respond(new PingCommandResponse
		{
			ProcessId = PayloadEnvironment.ProcessId,
			IsWpfAvailable = availability.IsWpfAvailable,
			IsWinFormsAvailable = availability.IsWinFormsAvailable,
			IsNativeFallbackAvailable = availability.IsNativeFallbackAvailable,
			IsDispatcherAvailable = availability.IsDispatcherAvailable,
			RootCount = availability.RootCount,
		});
	}
}
