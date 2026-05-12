namespace DeepFlowTest.AppDriverPayload.Commands;

using DeepFlowTest.Contracts;
using DeepFlowTest.Utility;

internal static class UnsupportedUiCommand
{
	public static object Process(string commandKind)
	{
		var availability = ThreadUtility.GetAvailability();
		if (!availability.IsWpfAvailable && !availability.IsWinFormsAvailable && availability.IsNativeFallbackAvailable)
		{
			return StandardIpcResponse.FromError(
				$"Command '{commandKind}' requires target inspection/action support. Native fallback was considered but this command is implemented by a later milestone.",
				ProtocolConstants.ErrorCodes.UnsupportedTarget,
				LogCorrelationId());
		}

		return StandardIpcResponse.FromError(
			$"Command '{commandKind}' is implemented by a later milestone.",
			ProtocolConstants.ErrorCodes.UnsupportedCommand,
			LogCorrelationId());
	}

	private static string LogCorrelationId()
	{
		return System.IO.Path.GetFileNameWithoutExtension(PayloadLog.CurrentLogPath);
	}
}
