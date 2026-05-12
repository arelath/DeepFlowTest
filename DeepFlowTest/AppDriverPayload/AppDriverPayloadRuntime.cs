namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;

public interface IAppDriverPayloadRuntime
{
	bool HasSupportedTarget();

	void StartOneShot(AppDriverPayloadStartupOptions options);

	ReusablePipeSession StartReusable(AppDriverPayloadStartupOptions options);
}

internal sealed class DefaultAppDriverPayloadRuntime : IAppDriverPayloadRuntime
{
	public bool HasSupportedTarget()
	{
		return ThreadUtility.HasSupportedUiRoot();
	}

	public void StartOneShot(AppDriverPayloadStartupOptions options)
	{
		var thread = new Thread(() => RunOneShotCommandLoop(options))
		{
			IsBackground = true,
			Name = $"{nameof(AppDriverPayload)}:{options.PipeName}",
		};
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
	}

	public ReusablePipeSession StartReusable(AppDriverPayloadStartupOptions options)
	{
		return ReusablePipeSessionRegistry.GetOrStart(options.PipeName);
	}

	private static void RunOneShotCommandLoop(AppDriverPayloadStartupOptions options)
	{
		PayloadLog.Write($"Starting one-shot command loop for pipe '{options.PipeName}'.");
		using var channel = new NamedPipeServer(options.PipeName);
		NamedPipeServer.Command? command = null;
		try
		{
			command = channel.WaitForNextCommand();
			AppDriverCommandDispatcher.Process(command.Value, options, reusableSession: null);
		}
		catch (Exception ex)
		{
			PayloadLog.Write("One-shot command loop failed.", ex);
			if (command.HasValue && !command.Value.CheckHasResponded())
			{
				command.Value.Respond(StandardIpcResponse.FromError(
					ex.ToString(),
					ex is ProtocolException protocolException ? protocolException.ErrorCode : ProtocolConstants.ErrorCodes.ProtocolError,
					PathCorrelationId()));
			}
		}
	}

	private static string PathCorrelationId()
	{
		return System.IO.Path.GetFileNameWithoutExtension(PayloadLog.CurrentLogPath);
	}
}
