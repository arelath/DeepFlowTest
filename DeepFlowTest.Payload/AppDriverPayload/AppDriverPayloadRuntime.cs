namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;

public interface IAppDriverPayloadRuntime
{
	UiAvailability GetAvailability();

	bool HasSupportedTarget(UiAvailability availability);

	void StartOneShot(AppDriverPayloadStartupOptions options);

	ReusablePipeSession StartReusable(AppDriverPayloadStartupOptions options);
}

internal sealed class DefaultAppDriverPayloadRuntime : IAppDriverPayloadRuntime
{
	public bool HasSupportedTarget()
	{
		return HasSupportedTarget(GetAvailability());
	}

	public UiAvailability GetAvailability()
	{
		return ThreadUtility.GetAvailability();
	}

	public bool HasSupportedTarget(UiAvailability availability)
	{
		return ThreadUtility.HasSupportedUiRoot(availability);
	}

	public void StartOneShot(AppDriverPayloadStartupOptions options)
	{
		var channel = new NamedPipeServer(options.PipeName);
		var thread = new Thread(() => RunOneShotCommandLoop(options, channel))
		{
			IsBackground = true,
			Name = $"{nameof(AppDriverPayload)}:{options.PipeName}",
		};
		thread.SetApartmentState(ApartmentState.STA);
		try
		{
			thread.Start();
		}
		catch
		{
			channel.Dispose();
			throw;
		}
	}

	public ReusablePipeSession StartReusable(AppDriverPayloadStartupOptions options)
	{
		return ReusablePipeSessionRegistry.GetOrStart(options.PipeName);
	}

	private static void RunOneShotCommandLoop(AppDriverPayloadStartupOptions options, NamedPipeServer channel)
	{
		PayloadLog.Write($"Starting one-shot command loop for pipe '{options.PipeName}'.");
		using (channel)
		{
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
						PayloadLog.CurrentCorrelationId));
				}
			}
		}
	}
}
