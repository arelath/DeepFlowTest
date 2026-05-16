namespace DeepFlowTest.Tests;

using System;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal static class TestIpcHost
{
	public static object? CaptureResponse(
		object request,
		ReusablePipeSession? reusableSession = null,
		string pipeName = "test-pipe",
		string logPrefix = "deepflowtest-test") =>
		CaptureDispatch(request, reusableSession, pipeName, logPrefix).Response;

	public static CommandDispatchCapture CaptureDispatch(
		object request,
		ReusablePipeSession? reusableSession = null,
		string pipeName = "test-pipe",
		string logPrefix = "deepflowtest-test")
	{
		PayloadLog.Initialize($"{logPrefix}-{Guid.NewGuid():N}");
		object? response = null;
		var responseCount = 0;
		var command = new NamedPipeServer.Command
		{
			Value = request,
			Respond = value =>
			{
				response = value;
				responseCount++;
			},
			CheckHasResponded = () => responseCount != 0,
			HoldConnectionOpen = () => { },
			TrySend = value =>
			{
				response = value;
				responseCount++;
				return true;
			},
		};
		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = pipeName,
			Mode = reusableSession is null ? PayloadStartupModes.OneShotDriver : PayloadStartupModes.ReusableCli,
			PayloadRoot = AppContext.BaseDirectory,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};

		AppDriverCommandDispatcher.Process(command, options, reusableSession);
		return new CommandDispatchCapture(response, responseCount);
	}
}

internal sealed record CommandDispatchCapture(object? Response, int ResponseCount);
