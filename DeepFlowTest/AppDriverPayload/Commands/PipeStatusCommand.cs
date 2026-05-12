namespace DeepFlowTest.AppDriverPayload.Commands;

using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal static class PipeStatusCommand
{
	public static object Process(PipeStatusCommandRequest request, AppDriverPayloadStartupOptions options, ReusablePipeSession? reusableSession)
	{
		return reusableSession?.CreateStatusResponse() ?? new PipeStatusCommandResponse
		{
			PipeName = options.PipeName,
			IsReusable = false,
			IsBusy = false,
			IsSending = false,
			ActiveSubscriptionCount = 0,
			TotalCommandsHandled = 1,
			DisconnectedClientCount = 0,
			IdleMode = "one-shot-command",
		};
	}
}
