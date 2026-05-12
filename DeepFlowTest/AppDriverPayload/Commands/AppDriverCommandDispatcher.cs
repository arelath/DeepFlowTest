namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using Newtonsoft.Json.Linq;

internal static class AppDriverCommandDispatcher
{
	private const int DefaultCommandTimeoutMs = 1000;
	private const int LargeCommandTimeoutMs = 5000;

	private static readonly TreeService TreeService = new();
	private static readonly ExpressionCache ExpressionCache = new();
	private static int delayBeforeUiHandlerForTests;

	private static readonly Dictionary<string, CommandHandler> ImmediateHandlers = new()
	{
		[ProtocolConstants.Commands.Hello] = (command, options, reusableSession) =>
			HelloCommand.Process(MessagePacker.ConvertTo<HelloCommandRequest>(command.Value), options, reusableSession),
		[ProtocolConstants.Commands.PipeStatus] = (command, options, reusableSession) =>
			PipeStatusCommand.Process(MessagePacker.ConvertTo<PipeStatusCommandRequest>(command.Value), options, reusableSession),
		[ProtocolConstants.Commands.StartSending] = (command, _, reusableSession) =>
			StartSendingCommand.Process(MessagePacker.ConvertTo<StartSendingCommandRequest>(command.Value), command, reusableSession),
		[ProtocolConstants.Commands.StopSending] = (command, _, reusableSession) =>
			StopSendingCommand.Process(MessagePacker.ConvertTo<StopSendingCommandRequest>(command.Value), reusableSession),
	};

	private static readonly Dictionary<string, AsyncCommandHandler> UiHandlers = new()
	{
		[ProtocolConstants.Commands.Ping] = (command, _, _) =>
			Task.FromResult(PingCommand.Process(MessagePacker.ConvertTo<PingCommandRequest>(command.Value))),
		[ProtocolConstants.Commands.GetVisualTree] = (command, _, _) =>
			Task.FromResult(GetVisualTreeCommand.Process(MessagePacker.ConvertTo<GetVisualTreeCommandRequest>(command.Value), TreeService)),
		[ProtocolConstants.Commands.FindElement] = (command, _, _) =>
			Task.FromResult(FindElementCommand.Process(MessagePacker.ConvertTo<FindElementCommandRequest>(command.Value), TreeService, ExpressionCache)),
		[ProtocolConstants.Commands.Screenshot] = (command, _, _) =>
			Task.FromResult(ScreenshotCommand.Process(MessagePacker.ConvertTo<ScreenshotCommandRequest>(command.Value), TreeService)),
		[ProtocolConstants.Commands.Click] = (command, _, _) =>
			Task.FromResult(TargetActionCommand.Click(MessagePacker.ConvertTo<ClickCommandRequest>(command.Value), TreeService)),
		[ProtocolConstants.Commands.Focus] = (command, _, _) =>
			Task.FromResult(TargetActionCommand.Focus(MessagePacker.ConvertTo<FocusCommandRequest>(command.Value), TreeService)),
		[ProtocolConstants.Commands.TypeText] = (command, _, _) =>
			Task.FromResult(TargetActionCommand.TypeText(MessagePacker.ConvertTo<TypeTextCommandRequest>(command.Value), TreeService)),
		[ProtocolConstants.Commands.KeyPress] = (command, _, _) =>
			Task.FromResult(TargetActionCommand.KeyPress(MessagePacker.ConvertTo<KeyPressCommandRequest>(command.Value), TreeService)),
		[ProtocolConstants.Commands.SetProperty] = (command, _, _) =>
			Task.FromResult(TargetActionCommand.SetProperty(MessagePacker.ConvertTo<SetPropertyCommandRequest>(command.Value), TreeService)),
		[ProtocolConstants.Commands.RaiseEvent] = (command, _, _) =>
			Task.FromResult(TargetActionCommand.RaiseEvent(MessagePacker.ConvertTo<RaiseEventCommandRequest>(command.Value), TreeService)),
		[ProtocolConstants.Commands.KnownRoutedEvent] = (command, _, _) =>
			Task.FromResult(TargetActionCommand.KnownRoutedEvent(MessagePacker.ConvertTo<KnownRoutedEventCommandRequest>(command.Value), TreeService)),
		[ProtocolConstants.Commands.KnownOperation] = (command, _, _) =>
			Task.FromResult(TargetActionCommand.KnownOperation(MessagePacker.ConvertTo<KnownOperationCommandRequest>(command.Value), TreeService)),
		[ProtocolConstants.Commands.Invoke] = (command, _, _) =>
			Task.FromResult(TargetActionCommand.Invoke(MessagePacker.ConvertTo<InvokeCommandRequest>(command.Value), TreeService)),
	};

	public static void Process(NamedPipeServer.Command command, AppDriverPayloadStartupOptions options, ReusablePipeSession? reusableSession)
	{
		try
		{
			var kind = GetCommandKind(command.Value);
			PayloadLog.Write($"Processing command '{kind}'.");

			if (ImmediateHandlers.TryGetValue(kind, out var immediateHandler))
			{
				RespondIfNeeded(command, immediateHandler(command, options, reusableSession));
				return;
			}

			if (UiHandlers.TryGetValue(kind, out var uiHandler))
			{
				var timeoutMs = GetTimeoutMs(command.Value, kind);
				var result = ThreadUtility.RunCommandWithTimeoutAsync(
						() => RunUiHandlerAsync(kind, uiHandler, command, options, reusableSession),
						timeoutMs,
						(message, exception) => PayloadLog.Write(message, exception),
						LogCorrelationId())
					.ConfigureAwait(false)
					.GetAwaiter()
					.GetResult();
				RespondIfNeeded(command, result);
				return;
			}

			RespondIfNeeded(
				command,
				StandardIpcResponse.FromError(
					$"Unsupported command kind: {kind}",
					ProtocolConstants.ErrorCodes.ProtocolError,
					LogCorrelationId()));
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

	private static async Task<object> RunUiHandlerAsync(
		string kind,
		AsyncCommandHandler handler,
		NamedPipeServer.Command command,
		AppDriverPayloadStartupOptions options,
		ReusablePipeSession? reusableSession)
	{
		if (delayBeforeUiHandlerForTests > 0)
			await Task.Delay(delayBeforeUiHandlerForTests).ConfigureAwait(false);

		if (kind == ProtocolConstants.Commands.Ping)
			return await handler(command, options, reusableSession).ConfigureAwait(false);

		object? result = null;
		var runResult = await ThreadUtility.RunOnUIThreadAsync(async () =>
		{
			result = await handler(command, options, reusableSession).ConfigureAwait(false);
		}).ConfigureAwait(false);

		if (runResult == UiThreadRunResult.Finished)
			return result ?? StandardIpcResponse.Ok();

		return UnsupportedUiCommand.Process(kind);
	}

	private static int GetTimeoutMs(object command, string kind)
	{
		var timeout = TryGetIntProperty(command, ProtocolConstants.Properties.TimeoutMs);
		if (timeout.HasValue)
			return timeout.Value;

		return kind == ProtocolConstants.Commands.GetVisualTree || kind == ProtocolConstants.Commands.Screenshot
			? LargeCommandTimeoutMs
			: DefaultCommandTimeoutMs;
	}

	private static int? TryGetIntProperty(object command, string propertyName)
	{
		if (command is JObject jObject && jObject.TryGetValue(propertyName, StringComparison.Ordinal, out var token))
			return token.Value<int?>();

		var property = command.GetType().GetProperty(propertyName);
		var value = property?.GetValue(command);
		if (value is int intValue)
			return intValue;

		return null;
	}

	private static void RespondIfNeeded(NamedPipeServer.Command command, object response)
	{
		if (!command.CheckHasResponded())
			command.Respond(response);
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

	private static IDisposable DelayUiHandlersForTests(int delayMs)
	{
		var previous = delayBeforeUiHandlerForTests;
		delayBeforeUiHandlerForTests = delayMs;
		return new RestoreDelay(previous);
	}

	private sealed class RestoreDelay : IDisposable
	{
		private readonly int previous;

		public RestoreDelay(int previous)
		{
			this.previous = previous;
		}

		public void Dispose()
		{
			delayBeforeUiHandlerForTests = previous;
		}
	}

	private delegate object CommandHandler(NamedPipeServer.Command command, AppDriverPayloadStartupOptions options, ReusablePipeSession? reusableSession);

	private delegate Task<object> AsyncCommandHandler(NamedPipeServer.Command command, AppDriverPayloadStartupOptions options, ReusablePipeSession? reusableSession);
}
