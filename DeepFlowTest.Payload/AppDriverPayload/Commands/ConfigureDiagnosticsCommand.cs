namespace DeepFlowTest.AppDriverPayload.Commands;

using DeepFlowTest.AppDriverPayload.Diagnostics;
using DeepFlowTest.Contracts;

internal static class ConfigureDiagnosticsCommand
{
	public static object Process(ConfigureDiagnosticsCommandRequest request)
	{
		if (request.VirtualPointer is not null)
		{
			var error = ValidateVirtualPointer(request.VirtualPointer);
			if (error is not null)
				return StandardIpcResponse.FromError(error, ProtocolConstants.ErrorCodes.InvalidArguments, PayloadLog.CurrentCorrelationId);

			VirtualPointerService.Configure(request.VirtualPointer);
		}

		return StandardIpcResponse.Ok();
	}

	private static string? ValidateVirtualPointer(VirtualPointerOptionsDto options)
	{
		if (options.HideDelayMs is < 0 or > 60_000)
			return "Virtual pointer hide delay must be between 0 and 60000 ms.";

		return null;
	}
}
