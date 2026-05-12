namespace DeepFlowTest.AppDriverPayload.Commands;

using DeepFlowTest.Contracts;
using DeepFlowTest.Utility;

internal static class UnsupportedUiCommand
{
	public static object Process(string commandKind)
	{
		var availability = ThreadUtility.GetAvailability();
		return StandardIpcResponse.FromError(
			$"Command '{commandKind}' requires WPF, WinForms, or native HWND target support. Availability: WPF={availability.IsWpfAvailable}; WinForms={availability.IsWinFormsAvailable}; NativeFallback={availability.IsNativeFallbackAvailable}.",
			ProtocolConstants.ErrorCodes.UnsupportedTarget,
			LogCorrelationId());
	}

	private static string LogCorrelationId()
	{
		return System.IO.Path.GetFileNameWithoutExtension(PayloadLog.CurrentLogPath);
	}
}
