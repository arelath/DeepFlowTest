namespace DeepFlowTest.AppDriverPayload;

using System;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using Newtonsoft.Json.Linq;

internal static class AppDriverCommandDispatcher
{
	public static void Process(NamedPipeServer.Command command, AppDriverPayloadStartupOptions options, ReusablePipeSession? reusableSession)
	{
		try
		{
			var kind = GetCommandKind(command.Value);
			PayloadLog.Write($"Processing command '{kind}'.");

			switch (kind)
			{
				case ProtocolConstants.Commands.Hello:
					HelloCommand.Process(MessagePacker.ConvertTo<HelloCommandRequest>(command.Value), command, options, reusableSession);
					break;
				case ProtocolConstants.Commands.Ping:
					PingCommand.Process(MessagePacker.ConvertTo<PingCommandRequest>(command.Value), command);
					break;
				case ProtocolConstants.Commands.PipeStatus:
					PipeStatusCommand.Process(MessagePacker.ConvertTo<PipeStatusCommandRequest>(command.Value), command, options, reusableSession);
					break;
				case ProtocolConstants.Commands.StartSending:
				case ProtocolConstants.Commands.StopSending:
					command.Respond(StandardIpcResponse.FromError($"Command '{kind}' is not implemented yet.", ProtocolConstants.ErrorCodes.UnsupportedCommand, LogCorrelationId()));
					break;
				case ProtocolConstants.Commands.Click:
				case ProtocolConstants.Commands.FindElement:
				case ProtocolConstants.Commands.Focus:
				case ProtocolConstants.Commands.GetVisualTree:
				case ProtocolConstants.Commands.Invoke:
				case ProtocolConstants.Commands.KeyPress:
				case ProtocolConstants.Commands.KnownOperation:
				case ProtocolConstants.Commands.KnownRoutedEvent:
				case ProtocolConstants.Commands.RaiseEvent:
				case ProtocolConstants.Commands.Screenshot:
				case ProtocolConstants.Commands.SetProperty:
				case ProtocolConstants.Commands.TypeText:
					command.Respond(StandardIpcResponse.FromError($"UI command '{kind}' is not implemented in this milestone slice.", ProtocolConstants.ErrorCodes.UnsupportedCommand, LogCorrelationId()));
					break;
				default:
					command.Respond(StandardIpcResponse.FromError($"Unsupported command kind: {kind}", ProtocolConstants.ErrorCodes.UnsupportedCommand, LogCorrelationId()));
					break;
			}
		}
		catch (Exception ex)
		{
			PayloadLog.Write("Command dispatch failed.", ex);
			if (!command.CheckHasResponded())
			{
				command.Respond(StandardIpcResponse.FromError(
					ex.ToString(),
					ex is ProtocolException protocolException ? protocolException.ErrorCode : ProtocolConstants.ErrorCodes.ProtocolError,
					LogCorrelationId()));
			}
		}
	}

	public static string GetCommandKind(object command)
	{
		if (command is IpcCommand ipcCommand)
			return ipcCommand.Kind;

		if (command is JObject jObject && jObject.TryGetValue(ProtocolConstants.Properties.Kind, StringComparison.Ordinal, out var kindToken))
			return kindToken.Value<string>() ?? string.Empty;

		var property = command.GetType().GetProperty(ProtocolConstants.Properties.Kind);
		return property?.GetValue(command)?.ToString() ?? string.Empty;
	}

	private static string LogCorrelationId()
	{
		return System.IO.Path.GetFileNameWithoutExtension(PayloadLog.CurrentLogPath);
	}
}
