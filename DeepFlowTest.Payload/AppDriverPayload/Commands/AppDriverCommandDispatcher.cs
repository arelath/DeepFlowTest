namespace DeepFlowTest.AppDriverPayload;

using System;
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
	private static readonly TreeService TreeService = new();
	private static readonly ExpressionCache ExpressionCache = new();
	private static readonly CommandHandlerRegistry HandlerRegistry = CommandHandlerRegistry.CreateDefault();
	private static int delayBeforeUiHandlerForTests;

	public static void Process(NamedPipeServer.Command command, AppDriverPayloadStartupOptions options, ReusablePipeSession? reusableSession)
	{
		var context = new CommandContext(command, options, reusableSession, TreeService, ExpressionCache);
		try
		{
			var kind = GetCommandKind(command.Value);
			PayloadLog.Write($"Processing command '{kind}'.");

			if (HandlerRegistry.ImmediateHandlers.TryGetValue(kind, out var immediateHandler))
			{
				RespondIfNeeded(command, immediateHandler.Handle(context));
				return;
			}

			if (HandlerRegistry.UiHandlers.TryGetValue(kind, out var uiHandler))
			{
				if (TryProcessNativeCommand(kind, context, allowUntargetedCommands: false, out var nativeResponse))
				{
					RespondIfNeeded(command, nativeResponse);
					return;
				}

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
		RegisteredCommandHandler handler,
		CommandContext context,
		CancellationToken token)
	{
		if (delayBeforeUiHandlerForTests > 0)
			await Task.Delay(delayBeforeUiHandlerForTests).ConfigureAwait(false);

		if (kind == ProtocolConstants.Commands.Ping)
			return handler.Handle(context);

		object? result = null;
		var runResult = await ThreadUtility.RunOnUIThreadAsync(() =>
		{
			result = handler.Handle(context);
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		if (runResult == UiThreadRunResult.Finished)
		{
			if (result is IDeferredCommandAction deferred)
				return await deferred.ExecuteAsync(token).ConfigureAwait(false);

			return result ?? StandardIpcResponse.Ok();
		}

		return UnsupportedUiCommand.Process(kind);
	}

	private static async Task<object> RunUiHandlerWithModalWatchAsync(
		string kind,
		RegisteredCommandHandler handler,
		CommandContext context,
		int timeoutMs)
	{
		AppHooks.ShowDialogCalled = false;
		using var cancellation = new CancellationTokenSource();
		var commandTask = ThreadUtility.RunCommandWithTimeoutAsync(
			() => RunUiHandlerAsync(kind, handler, context, cancellation.Token),
			timeoutMs,
			(message, exception) => PayloadLog.Write(message, exception),
			context.LogCorrelationId);
		var modalTask = WaitForShowDialogAsync(timeoutMs, cancellation.Token);

		var completed = await Task.WhenAny(commandTask, modalTask).ConfigureAwait(false);
		if (completed == modalTask && await modalTask.ConfigureAwait(false) == UiThreadRunResult.Pending)
		{
			if (TryProcessNativeCommand(kind, context, allowUntargetedCommands: true, out var nativeResponse))
			{
				cancellation.Cancel();
				AppHooks.ShowDialogCalled = false;
				return nativeResponse;
			}

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

		var commandResult = await commandTask.ConfigureAwait(false);
		return AugmentWithNativeDialogResultIfNeeded(kind, context, commandResult);
	}

	// A native MessageBox/dialog pumps the thread's message queue, so WPF Dispatcher operations
	// (including our find snapshot) still run and complete against the WPF visual tree -- which does
	// NOT contain the native #32770 window. The find then returns NoMatch fast enough to win the race
	// in WaitForShowDialogAsync, so the modal-watch's native fallback never fires and the dialog is
	// never surfaced. To make detection deterministic rather than race-dependent: when an untargeted
	// find/visual-tree command completed without matching anything and native dialog roots exist,
	// re-run it against the native tree and prefer that result. WPF-first, native only as augmentation.
	private static object AugmentWithNativeDialogResultIfNeeded(string kind, CommandContext context, object commandResult)
	{
		if (kind is not (ProtocolConstants.Commands.FindElement or ProtocolConstants.Commands.GetVisualTree))
			return commandResult;

		// Only untargeted commands: a targeted command already resolved to a specific (WPF) element.
		if (!string.IsNullOrEmpty(TryGetStringProperty(context.Command.Value, ProtocolConstants.Properties.TargetId)))
			return commandResult;

		if (!IsEmptyFindResult(commandResult))
			return commandResult;

		if (!NativeDialogService.HasRootWindowsForCurrentProcess())
			return commandResult;

		if (TryProcessNativeCommand(kind, context, allowUntargetedCommands: true, out var nativeResponse)
			&& !IsEmptyFindResult(nativeResponse))
		{
			return nativeResponse;
		}

		return commandResult;
	}

	private static bool IsEmptyFindResult(object response) =>
		response switch
		{
			FindElementCommandResponse find => find.MatchCount == 0,
			_ => false,
		};

	internal static async Task<UiThreadRunResult> WaitForShowDialogAsync(int timeoutMs, CancellationToken token)
	{
		var start = DateTime.UtcNow;
		var nativeDialogGraceMs = Math.Min(timeoutMs, TimeoutDefaults.PayloadNativeDialogGraceMs);
		while (!AppHooks.ShowDialogCalled && (DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
		{
			if (token.IsCancellationRequested)
				return UiThreadRunResult.Finished;

			if ((DateTime.UtcNow - start).TotalMilliseconds >= nativeDialogGraceMs &&
				NativeDialogService.HasRootWindowsForCurrentProcess())
			{
				return UiThreadRunResult.Pending;
			}

			await Task.Delay(TimeoutDefaults.PayloadModalPollDelayMs, token).ConfigureAwait(false);
		}

		return AppHooks.ShowDialogCalled ? UiThreadRunResult.Pending : UiThreadRunResult.Finished;
	}

	private static bool TryProcessNativeCommand(
		string kind,
		CommandContext context,
		bool allowUntargetedCommands,
		out object response)
	{
		response = null!;
		if (!HandlerRegistry.UiHandlers.TryGetValue(kind, out var handler) || !ShouldProcessNatively(kind, context.Command.Value, allowUntargetedCommands))
			return false;

		var treeService = NativeDialogService.TryCreateTreeService();
		var targetId = TryGetStringProperty(context.Command.Value, ProtocolConstants.Properties.TargetId);
		if (treeService is null)
		{
			if (!IsNativeWindowTargetId(targetId))
				return false;

			treeService = new TreeService();
		}

		var nativeContext = new CommandContext(context.Command, context.Options, context.ReusableSession, treeService, context.ExpressionCache);
		try
		{
			response = handler.Handle(nativeContext);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			response = StandardIpcResponse.FromError(ex.ToString(), ProtocolConstants.ErrorCodes.ProtocolError, context.LogCorrelationId);
		}

		return true;
	}

	private static bool ShouldProcessNatively(string kind, object command, bool allowUntargetedCommands)
	{
		var targetId = TryGetStringProperty(command, ProtocolConstants.Properties.TargetId);
		if (IsNativeWindowTargetId(targetId))
		{
			return kind is ProtocolConstants.Commands.Click
				or ProtocolConstants.Commands.DragAndDrop
				or ProtocolConstants.Commands.Focus
				or ProtocolConstants.Commands.TypeText
				or ProtocolConstants.Commands.SetProperty
				or ProtocolConstants.Commands.KnownOperation
				or ProtocolConstants.Commands.Screenshot;
		}

		if (!allowUntargetedCommands)
			return false;

		return kind is ProtocolConstants.Commands.GetVisualTree
			or ProtocolConstants.Commands.FindElement
			or ProtocolConstants.Commands.Screenshot
			or ProtocolConstants.Commands.KeyPress;
	}

	private static bool IsNativeWindowTargetId(string? targetId) =>
		targetId?.StartsWith("dft-hwnd-", StringComparison.Ordinal) == true;

	private static int GetTimeoutMs(object command, string kind)
	{
		var timeout = TryGetIntProperty(command, ProtocolConstants.Properties.TimeoutMs);
		if (timeout.HasValue)
			return timeout.Value;

		return kind == ProtocolConstants.Commands.GetVisualTree || kind == ProtocolConstants.Commands.Screenshot
			? TimeoutDefaults.PayloadLargeCommandTimeoutMs
			: TimeoutDefaults.PayloadCommandTimeoutMs;
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

	private static string? TryGetStringProperty(object command, string propertyName)
	{
		if (command is JObject jObject && jObject.TryGetValue(propertyName, StringComparison.Ordinal, out var token))
			return token.Value<string>();

		var property = command.GetType().GetProperty(propertyName);
		return property?.GetValue(command)?.ToString();
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
}
