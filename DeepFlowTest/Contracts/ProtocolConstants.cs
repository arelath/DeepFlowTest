namespace DeepFlowTest.Contracts;

public static class ProtocolConstants
{
	public const string ProductName = DeepFlowTest.ProductInfo.Name;
	public const string PipePrefix = "deepflowtest";
	public const string ProtocolVersion = "1";

	public static class Commands
	{
		public const string Click = "ClickCommand";
		public const string FindElement = "FindElementCommand";
		public const string Focus = "FocusCommand";
		public const string GetVisualTree = "GetVisualTreeCommand";
		public const string Hello = "HelloCommand";
		public const string Invoke = "InvokeCommand";
		public const string KeyPress = "KeyPressCommand";
		public const string KnownOperation = "KnownOperationCommand";
		public const string KnownRoutedEvent = "KnownRoutedEventCommand";
		public const string Ping = "PingCommand";
		public const string PipeStatus = "PipeStatusCommand";
		public const string RaiseEvent = "RaiseEventCommand";
		public const string Screenshot = "ScreenshotCommand";
		public const string SetProperty = "SetPropertyCommand";
		public const string StartSending = "StartSendingCommand";
		public const string StopSending = "StopSendingCommand";
		public const string TypeText = "TypeTextCommand";
	}

	public static class Properties
	{
		public const string Error = "Error";
		public const string ErrorCode = "ErrorCode";
		public const string Kind = "Kind";
		public const string Status = "Status";
		public const string Success = "Success";
		public const string TimeoutMs = "TimeoutMs";
	}

	public static class Statuses
	{
		public const string Ok = "ok";
		public const string Error = "error";
		public const string Started = "started";
		public const string Stopped = "stopped";
		public const string UnknownSubscription = "unknown-subscription";
	}

	public static class ErrorCodes
	{
		public const string CommandTimeout = "command-timeout";
		public const string MalformedFrame = "malformed-frame";
		public const string ProtocolError = "protocol-error";
		public const string StartupError = "startup-error";
		public const string TargetExited = "target-exited";
		public const string UnsupportedCommand = "unsupported-command";
		public const string UnsupportedProtocol = "unsupported-protocol";
		public const string UnsupportedTarget = "unsupported-target";
	}
}
