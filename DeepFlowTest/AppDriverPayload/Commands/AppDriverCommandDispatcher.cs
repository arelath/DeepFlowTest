namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections.Generic;
using System.Threading;
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
		[ProtocolConstants.Commands.Hello] = context =>
			HelloCommand.Process(context.Request<HelloCommandRequest>(), context.Options, context.ReusableSession),
		[ProtocolConstants.Commands.PipeStatus] = context =>
			PipeStatusCommand.Process(context.Request<PipeStatusCommandRequest>(), context.Options, context.ReusableSession),
		[ProtocolConstants.Commands.StartSending] = context =>
			StartSendingCommand.Process(context.Request<StartSendingCommandRequest>(), context.Command, context.ReusableSession, context.TreeService),
		[ProtocolConstants.Commands.StopSending] = context =>
			StopSendingCommand.Process(context.Request<StopSendingCommandRequest>(), context.ReusableSession),
	};

	private static readonly Dictionary<string, AsyncCommandHandler> UiHandlers = new()
	{
		[ProtocolConstants.Commands.Ping] = context =>
			Task.FromResult(PingCommand.Process(context.Request<PingCommandRequest>())),
		[ProtocolConstants.Commands.GetVisualTree] = context =>
			Task.FromResult(GetVisualTreeCommand.Process(context.Request<GetVisualTreeCommandRequest>(), context.TreeService)),
		[ProtocolConstants.Commands.FindElement] = context =>
			Task.FromResult(FindElementCommand.Process(context.Request<FindElementCommandRequest>(), context.TreeService, context.ExpressionCache)),
		[ProtocolConstants.Commands.Screenshot] = context =>
			Task.FromResult(ScreenshotCommand.Process(context.Request<ScreenshotCommandRequest>(), context.TreeService)),
		[ProtocolConstants.Commands.Click] = context =>
			Task.FromResult(TargetActionCommand.Click(context.Request<ClickCommandRequest>(), context.TreeService)),
		[ProtocolConstants.Commands.Focus] = context =>
			Task.FromResult(TargetActionCommand.Focus(context.Request<FocusCommandRequest>(), context.TreeService)),
		[ProtocolConstants.Commands.TypeText] = context =>
			Task.FromResult(TargetActionCommand.TypeText(context.Request<TypeTextCommandRequest>(), context.TreeService)),
		[ProtocolConstants.Commands.KeyPress] = context =>
			Task.FromResult(TargetActionCommand.KeyPress(context.Request<KeyPressCommandRequest>(), context.TreeService)),
		[ProtocolConstants.Commands.SetProperty] = context =>
			Task.FromResult(TargetActionCommand.SetProperty(context.Request<SetPropertyCommandRequest>(), context.TreeService)),
		[ProtocolConstants.Commands.RaiseEvent] = context =>
			Task.FromResult(TargetActionCommand.RaiseEvent(context.Request<RaiseEventCommandRequest>(), context.TreeService)),
		[ProtocolConstants.Commands.KnownRoutedEvent] = context =>
			Task.FromResult(TargetActionCommand.KnownRoutedEvent(context.Request<KnownRoutedEventCommandRequest>(), context.TreeService)),
		[ProtocolConstants.Commands.KnownOperation] = context =>
			Task.FromResult(TargetActionCommand.KnownOperation(context.Request<KnownOperationCommandRequest>(), context.TreeService)),
		[ProtocolConstants.Commands.Invoke] = context =>
			Task.FromResult(TargetActionCommand.Invoke(context.Request<InvokeCommandRequest>(), context.TreeService)),
	};

	public static void Process(NamedPipeServer.Command command, AppDriverPayloadStartupOptions options, ReusablePipeSession? reusableSession)
	{
		var context = new CommandContext(command, options, reusableSession, TreeService, ExpressionCache);
		try
		{
			var kind = GetCommandKind(command.Value);
			PayloadLog.Write($"Processing command '{kind}'.");

			if (ImmediateHandlers.TryGetValue(kind, out var immediateHandler))
			{
				RespondIfNeeded(command, immediateHandler(context));
				return;
			}

			if (UiHandlers.TryGetValue(kind, out var uiHandler))
			{
				var timeoutMs = GetTimeoutMs(command.Value, kind);
				var result = RunUiHandlerWithModalWatchAsync(kind, uiHandler, context, timeoutMs)
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
					context.LogCorrelationId));
		}
		catch (Exception ex)
		{
			PayloadLog.Write("Command dispatch failed.", ex);
			if (!command.CheckHasResponded())
			{
				command.Respond(StandardIpcResponse.FromError(
					ex.ToString(),
					ex is ProtocolException protocolException ? protocolException.ErrorCode : ProtocolConstants.ErrorCodes.ProtocolError,
					context.LogCorrelationId));
			}
		}
	}

	private static async Task<object> RunUiHandlerAsync(
		string kind,
		AsyncCommandHandler handler,
		CommandContext context)
	{
		if (delayBeforeUiHandlerForTests > 0)
			await Task.Delay(delayBeforeUiHandlerForTests).ConfigureAwait(false);

		if (kind == ProtocolConstants.Commands.Ping)
			return await handler(context).ConfigureAwait(false);

		object? result = null;
		var runResult = await ThreadUtility.RunOnUIThreadAsync(async () =>
		{
			result = await handler(context).ConfigureAwait(false);
		}).ConfigureAwait(false);

		if (runResult == UiThreadRunResult.Finished)
			return result ?? StandardIpcResponse.Ok();

		return UnsupportedUiCommand.Process(kind);
	}

	private static async Task<object> RunUiHandlerWithModalWatchAsync(
		string kind,
		AsyncCommandHandler handler,
		CommandContext context,
		int timeoutMs)
	{
		AppHooks.ShowDialogCalled = false;
		using var cancellation = new CancellationTokenSource();
		var commandTask = ThreadUtility.RunCommandWithTimeoutAsync(
			() => RunUiHandlerAsync(kind, handler, context),
			timeoutMs,
			(message, exception) => PayloadLog.Write(message, exception),
			context.LogCorrelationId);
		var modalTask = WaitForShowDialogAsync(timeoutMs, cancellation.Token);

		var completed = await Task.WhenAny(commandTask, modalTask).ConfigureAwait(false);
		if (completed == modalTask && await modalTask.ConfigureAwait(false) == UiThreadRunResult.Pending)
		{
			cancellation.Cancel();
			AppHooks.ShowDialogCalled = false;
			return StandardIpcResponse.PendingResult(context.LogCorrelationId);
		}

		cancellation.Cancel();
		try
		{
			await modalTask.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			AppHooks.ShowDialogCalled = false;
		}

		return await commandTask.ConfigureAwait(false);
	}

	internal static async Task<UiThreadRunResult> WaitForShowDialogAsync(int timeoutMs, CancellationToken token)
	{
		var start = DateTime.UtcNow;
		while (!AppHooks.ShowDialogCalled && (DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
		{
			if (token.IsCancellationRequested)
				return UiThreadRunResult.Finished;

			await Task.Delay(50, token).ConfigureAwait(false);
		}

		return AppHooks.ShowDialogCalled ? UiThreadRunResult.Pending : UiThreadRunResult.Finished;
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

	internal static IDisposable DelayUiHandlersForTests(int delayMs)
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

	private sealed class CommandContext
	{
		public CommandContext(
			NamedPipeServer.Command command,
			AppDriverPayloadStartupOptions options,
			ReusablePipeSession? reusableSession,
			TreeService treeService,
			ExpressionCache expressionCache)
		{
			Command = command;
			Options = options ?? throw new ArgumentNullException(nameof(options));
			ReusableSession = reusableSession;
			TreeService = treeService ?? throw new ArgumentNullException(nameof(treeService));
			ExpressionCache = expressionCache ?? throw new ArgumentNullException(nameof(expressionCache));
			LogCorrelationId = PayloadLog.CurrentCorrelationId;
		}

		public NamedPipeServer.Command Command { get; }

		public AppDriverPayloadStartupOptions Options { get; }

		public ReusablePipeSession? ReusableSession { get; }

		public TreeService TreeService { get; }

		public ExpressionCache ExpressionCache { get; }

		public string LogCorrelationId { get; }

		public TRequest Request<TRequest>() => MessagePacker.ConvertTo<TRequest>(Command.Value);
	}

	private delegate object CommandHandler(CommandContext context);

	private delegate Task<object> AsyncCommandHandler(CommandContext context);
}
