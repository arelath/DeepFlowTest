namespace DeepFlowTest;

using System;
using System.Globalization;
using System.Runtime.Serialization;
using DeepFlowTest.Contracts;

internal sealed class ElementCommandExecutor(DriverCommandClient commandClient, ElementRepairService repairService)
{
	private readonly DriverCommandClient commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
	private readonly ElementRepairService repairService = repairService ?? throw new ArgumentNullException(nameof(repairService));

	public Element SendTargetedWithRepair(Element element, Func<IpcCommand> commandFactory)
	{
		SendTargetedWithRepairResponse(element, commandFactory);
		return element;
	}

	public StandardIpcResponse SendTargetedWithRepairResponse(Element element, Func<IpcCommand> commandFactory)
	{
		var response = SendWithRepair<StandardIpcResponse>(element, commandFactory);
		DriverCommandClient.ThrowIfStandardFailure(response, "Command failed.");
		return response;
	}

	public TResponse SendWithRepair<TResponse>(Element element, Func<IpcCommand> commandFactory)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		_ = commandFactory ?? throw new ArgumentNullException(nameof(commandFactory));

		var response = commandClient.Send<TResponse>(commandFactory());
		if (DriverCommandClient.IsFailure(response, ProtocolConstants.ErrorCodes.StaleTarget))
		{
			var repaired = repairService.Repair(element);
			element.ReplaceWith(repaired);
			response = commandClient.Send<TResponse>(commandFactory());
		}

		DriverCommandClient.ThrowIfFailure(response, "Command failed.");
		return response;
	}

	public ScreenshotCommandResponse CaptureScreenshot(Element element, string format = "png") =>
		SendWithRepair<ScreenshotCommandResponse>(element, () => new ScreenshotCommandRequest { TargetId = element.TargetId, Format = ImageFormatExtensions.ParseProtocolString(format) });

	public byte[] Screenshot(Element element, ImageFormat format = ImageFormat.Jpeg) =>
		MediaCaptureService.DecodeScreenshot(MediaCaptureService.WaitForStableScreenshot(() => SendWithRepair<ScreenshotCommandResponse>(element, () => new ScreenshotCommandRequest { TargetId = element.TargetId, Format = format }), nameof(Screenshot)));

	public void SaveScreenshot(Element element, string fileOutputPath)
	{
		_ = fileOutputPath ?? throw new ArgumentNullException(nameof(fileOutputPath));
		var bytes = Screenshot(element, MediaCaptureService.GetImageFormatFromPath(fileOutputPath));
		MediaCaptureService.WriteBytes(fileOutputPath, bytes);
	}

	public static T? ConvertResponseValue<T>(object? value)
	{
		if (value is null)
			return default;
		if (value is T typed)
			return typed;
		if (value is Newtonsoft.Json.Linq.JToken token)
			return token.ToObject<T>();
		return (T?)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
	}

	public static void ThrowIfUnserializableResult(StandardIpcResponse response, string caller)
	{
		if (string.Equals(response.Status, ProtocolConstants.Statuses.UnserializableResult, StringComparison.Ordinal))
			throw new SerializationException($"Unserializable {caller} result received.");
	}
}
