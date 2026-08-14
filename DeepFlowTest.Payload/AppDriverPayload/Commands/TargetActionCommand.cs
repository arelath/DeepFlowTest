namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static partial class TargetActionCommand
{
	public static object Click(ClickCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.Click, request.TargetId, treeService, target =>
		{
			var button = request.MouseButton;
			var buttonName = ProtocolValueMapper.FormatMouseButton(button);

			return InvokeTargetAdapter(target, adapter => adapter.Click(target, button, request.ClickCount), $"{buttonName} click");
		});

	public static object DragAndDrop(DragAndDropCommandRequest request, TreeService treeService)
	{
		var validationError = ValidateDragAndDropRequest(request);
		if (validationError is not null)
			return StandardIpcResponse.FromError(validationError, ProtocolConstants.ErrorCodes.InvalidArguments, PayloadLog.CurrentCorrelationId);

		var sourceResolution = ResolveDragTarget(ProtocolConstants.Commands.DragAndDrop, request.TargetId, treeService, "source");
		if (sourceResolution.Response is not null)
			return sourceResolution.Response;

		var destinationResolution = ResolveDragTarget(ProtocolConstants.Commands.DragAndDrop, request.DestinationTargetId, treeService, "destination");
		if (destinationResolution.Response is not null)
			return destinationResolution.Response;

		var sourceTarget = sourceResolution.Target!;
		var destinationTarget = destinationResolution.Target!;

		if (request.UseInjectedEvents && sourceTarget is System.Windows.UIElement && destinationTarget is System.Windows.UIElement)
		{
			var injectedResult = WpfTargetAdapter.PerformInjectedDragAndDrop(
				sourceTarget,
				destinationTarget,
				new PointerAnchor(request.SourceAnchorX, request.SourceAnchorY),
				new PointerAnchor(request.DestinationAnchorX, request.DestinationAnchorY),
				request.DurationMs,
				request.StepIntervalMs);
			if (!injectedResult.Success && IsDetachedPresentationSourceError(injectedResult.Error))
			{
				return StandardIpcResponse.FromError(
					FormatActionError(injectedResult.Error ?? "The requested action is not supported for this target.", ProtocolConstants.Commands.DragAndDrop, request.TargetId),
					ProtocolConstants.ErrorCodes.StaleTarget,
					PayloadLog.CurrentCorrelationId);
			}

			return ToResponse(injectedResult, ProtocolConstants.Commands.DragAndDrop, request.TargetId);
		}

		var sourcePoint = GetPointerTarget(sourceTarget, new PointerAnchor(request.SourceAnchorX, request.SourceAnchorY));
		if (!sourcePoint.Success || sourcePoint.Value is null)
			return UnsupportedTarget($"DragAndDropCommand: source target '{request.TargetId}': {sourcePoint.Error ?? "Screen coordinates could not be resolved."}");

		var destinationPoint = GetPointerTarget(destinationTarget, new PointerAnchor(request.DestinationAnchorX, request.DestinationAnchorY));
		if (!destinationPoint.Success || destinationPoint.Value is null)
			return UnsupportedTarget($"DragAndDropCommand: destination target '{request.DestinationTargetId}': {destinationPoint.Error ?? "Screen coordinates could not be resolved."}");

		var plan = new DragPlan(
			sourcePoint.Value,
			destinationPoint.Value,
			request.DurationMs,
			request.HoldMs,
			request.StepIntervalMs,
			request.PostDropWaitMs,
			request.EnsureForeground,
			request.ValidateSameProcess);
		return new DeferredDragAndDropAction(plan, request.TargetId);
	}

	public static object Focus(FocusCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.Focus, request.TargetId, treeService, target =>
			InvokeTargetAdapter(target, adapter => adapter.Focus(target), "focus"));

	public static object TypeText(TypeTextCommandRequest request, TreeService treeService)
	{
		if (!string.IsNullOrWhiteSpace(request.TargetId))
			return WithTarget(ProtocolConstants.Commands.TypeText, request.TargetId!, treeService, target => TypeTextIntoTarget(target, request.Text, request.ClearFirst));

		using var syntheticInput = AppHooks.BeginSyntheticInput();
		return ToResponse(TypeTextIntoFocusedTarget(request.Text, request.ClearFirst));
	}

	public static object KeyPress(KeyPressCommandRequest request, TreeService treeService)
	{
		if (!string.IsNullOrWhiteSpace(request.TargetId))
			return WithTarget(ProtocolConstants.Commands.KeyPress, request.TargetId!, treeService, target => SendKeysToTarget(target, request.Keys, request.DelayMs, request.EnsureForeground));

		using var syntheticInput = AppHooks.BeginSyntheticInput();
		return ToResponse(SendKeysToForeground(request.Keys, request.DelayMs));
	}

	public static object SetProperty(SetPropertyCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.SetProperty, request.TargetId, treeService, target => SetProperty(target, request.PropertyName, request.PropertyValue));

	public static object RaiseEvent(RaiseEventCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.RaiseEvent, request.TargetId, treeService, target =>
		{
			if (TargetExpressionEvaluator.CanEvaluate(request.GetRoutedEventArgs))
				return RaiseExpressionRoutedEvent(target, request.GetRoutedEventArgs, request.TimeoutMs);

			var eventName = !string.IsNullOrWhiteSpace(request.EventName)
				? request.EventName
				: Convert.ToString(request.GetRoutedEventArgs, CultureInfo.InvariantCulture) ?? string.Empty;
			return RaiseKnownRoutedEvent(target, eventName);
		});

	public static object KnownRoutedEvent(KnownRoutedEventCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.KnownRoutedEvent, request.TargetId, treeService, target => RaiseKnownRoutedEvent(target, request.EventName));

	public static object KnownOperation(KnownOperationCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.KnownOperation, request.TargetId, treeService, target => RunKnownOperation(target, request.Operation));

	public static object Invoke(InvokeCommandRequest request, TreeService treeService)
	{
		if (!request.AllowUnsafeCode)
			return StandardIpcResponse.FromError("Invoke requires explicit unsafe-code opt-in.", ProtocolConstants.ErrorCodes.UnsupportedCommand, PayloadLog.CurrentCorrelationId);

		return WithTarget(ProtocolConstants.Commands.Invoke, request.TargetId, treeService, target =>
		{
			if (request.Detached)
				return PostDetachedInvoke(target, request);

			if (TargetExpressionEvaluator.CanEvaluate(request.Code))
			{
				var result = TargetExpressionEvaluator.Evaluate(target, request.Code, request.TimeoutMs, awaitTasks: true);
				return ActionResult.Ok(result);
			}

			var methodName = Convert.ToString(TargetValueConverter.UnwrapJsonValue(request.Code), CultureInfo.InvariantCulture);
			if (string.IsNullOrWhiteSpace(methodName))
				return ActionResult.Unsupported("Invoke requires a public parameterless method name.");

			var method = target.GetType().GetMethod(methodName!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy, null, Type.EmptyTypes, null);
			if (method is null)
				return ActionResult.Unsupported($"Method '{methodName}' was not found or is not parameterless.");

			try
			{
				var result = method.Invoke(target, null);
				return ActionResult.Ok(TargetMethodInvoker.AwaitTaskResult(result, request.TimeoutMs));
			}
			catch (TargetInvocationException ex) when (ex.InnerException is not null)
			{
				return ActionResult.Unsupported($"Invoke method '{methodName}' failed: {ex.InnerException.Message}");
			}
		});
	}

	// Detached invokes queue the code on the target's dispatcher and report success as soon as it is queued.
	// Running it inline would put the payload's command handler on the stack, so anything the code throws is
	// captured as a command failure and never reaches the target's own unhandled-exception handling. Tests that
	// drive an intentional crash (or an app shutdown) need the opposite: the exception has to escape to the
	// dispatcher exactly as it would from a real click.
	private static ActionResult PostDetachedInvoke(object target, InvokeCommandRequest request)
	{
		if (target is not DispatcherObject dispatcherObject)
			return ActionResult.Unsupported("Detached invoke requires a target that belongs to a dispatcher.");

		Action detachedAction;
		if (TargetExpressionEvaluator.TryGetDelegate(request.Code, out var expression))
		{
			detachedAction = () => InvokeWithoutWrapping(() => expression.DynamicInvoke(target));
		}
		else
		{
			var methodName = Convert.ToString(TargetValueConverter.UnwrapJsonValue(request.Code), CultureInfo.InvariantCulture);
			if (string.IsNullOrWhiteSpace(methodName))
				return ActionResult.Unsupported("Detached invoke requires a public parameterless method name.");

			var method = target.GetType().GetMethod(methodName!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy, null, Type.EmptyTypes, null);
			if (method is null)
				return ActionResult.Unsupported($"Method '{methodName}' was not found or is not parameterless.");

			detachedAction = () => InvokeWithoutWrapping(() => method.Invoke(target, null));
		}

		dispatcherObject.Dispatcher.BeginInvoke(DispatcherPriority.Send, detachedAction);
		return ActionResult.Ok();
	}

	// Reflection-based invocation wraps whatever the code throws in a TargetInvocationException. Rethrowing the
	// inner exception keeps the type and stack the target's handler would have seen from a direct call.
	private static void InvokeWithoutWrapping(Func<object?> invocation)
	{
		try
		{
			invocation();
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
		}
	}

	private static object WithTarget(string commandName, string targetId, TreeService treeService, Func<object, ActionResult> action)
	{
		if (string.IsNullOrWhiteSpace(targetId))
			return UnsupportedTarget($"{commandName}: a target ID is required.");

		var resolution = treeService.ResolveTarget(targetId);
		if (resolution.Status != TargetIdResolutionStatus.Found)
		{
			var errorCode = resolution.Status == TargetIdResolutionStatus.Stale
				? ProtocolConstants.ErrorCodes.StaleTarget
				: ProtocolConstants.ErrorCodes.UnsupportedTarget;
			return StandardIpcResponse.FromError($"{commandName}: target '{targetId}' resolved as {resolution.Status}.", errorCode, PayloadLog.CurrentCorrelationId);
		}

		try
		{
			using var syntheticInput = AppHooks.BeginSyntheticInput();
			return ToResponse(action(resolution.Target!), commandName, targetId);
		}
		catch (TimeoutException ex)
		{
			return StandardIpcResponse.FromError(
				$"{commandName}: action timed out for target '{targetId}': {ex.Message}",
				ProtocolConstants.ErrorCodes.CommandTimeout,
				PayloadLog.CurrentCorrelationId);
		}
		catch (SerializationException ex)
		{
			return StandardIpcResponse.FromError(
				$"{commandName}: action result is not serializable for target '{targetId}': {ex.Message}",
				ProtocolConstants.ErrorCodes.ProtocolError,
				PayloadLog.CurrentCorrelationId);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			return StandardIpcResponse.FromError(
				$"{commandName}: action failed for target '{targetId}': {ex.Message}",
				ProtocolConstants.ErrorCodes.UnsupportedTarget,
				PayloadLog.CurrentCorrelationId);
		}
	}

	private static StandardIpcResponse ToResponse(ActionResult result, string? commandName = null, string? targetId = null) =>
		result.Success
			? SerializableSuccess(result.Value)
			: UnsupportedTarget(FormatActionError(result.Error ?? "The requested action is not supported for this target.", commandName, targetId));

	private static StandardIpcResponse SerializableSuccess(object? value)
	{
		var response = new StandardIpcResponse
		{
			Success = true,
			Status = ProtocolConstants.Statuses.Ok,
			Value = value,
		};

		if (value is null || CanPackResponse(response))
			return response;

		return StandardIpcResponse.UnserializableResult();
	}

	private static bool CanPackResponse(StandardIpcResponse response)
	{
		try
		{
			MessagePacker.Pack(response);
			return true;
		}
		catch (Exception ex) when (ex is ProtocolException or Newtonsoft.Json.JsonException or InvalidOperationException or NotSupportedException)
		{
			return false;
		}
	}

	private static StandardIpcResponse UnsupportedTarget(string error) =>
		StandardIpcResponse.FromError(error, ProtocolConstants.ErrorCodes.UnsupportedTarget, PayloadLog.CurrentCorrelationId);

	private static bool IsDetachedPresentationSourceError(string? error) =>
		error?.IndexOf("not connected to a PresentationSource", StringComparison.OrdinalIgnoreCase) >= 0;

	private static string FormatActionError(string error, string? commandName, string? targetId)
	{
		if (string.IsNullOrWhiteSpace(commandName) && string.IsNullOrWhiteSpace(targetId))
			return error;

		return $"{commandName ?? "action"}: target '{targetId ?? string.Empty}': {error}";
	}

	private static ActionResult TypeTextIntoTarget(object target, string text, bool clearFirst) =>
		InvokeTargetAdapter(target, adapter => adapter.TypeText(target, text ?? string.Empty, clearFirst), "text input");

	private static ActionResult SendKeysToTarget(object target, object? keys, int delayMs, bool ensureForeground)
	{
		var keyText = Convert.ToString(TargetValueConverter.UnwrapJsonValue(keys), CultureInfo.InvariantCulture) ?? string.Empty;
		if (string.IsNullOrEmpty(keyText))
			return ActionResult.Unsupported("Key input cannot be empty.");

		if (delayMs > 0)
			Thread.Sleep(delayMs);

		return InvokeTargetAdapter(target, adapter => adapter.SendKeys(target, keys, keyText, delayMs), "key input");
	}

	private static ActionResult SetProperty(object target, string propertyName, object? rawValue)
	{
		if (string.IsNullOrWhiteSpace(propertyName))
			return ActionResult.Unsupported("Property name is required.");

		var value = TargetExpressionEvaluator.CanEvaluate(rawValue)
			? TargetExpressionEvaluator.Evaluate(target, rawValue, timeoutMs: null, awaitTasks: true)
			: TargetValueConverter.UnwrapJsonValue(rawValue);

		return InvokeTargetAdapter(target, adapter => adapter.SetProperty(target, propertyName, value), $"set property '{propertyName}'");
	}

	private static ActionResult RaiseKnownRoutedEvent(object target, string eventName) =>
		InvokeTargetAdapter(target, adapter => adapter.RaiseKnownRoutedEvent(target, eventName), $"routed event '{eventName}'");

	private static ActionResult RaiseExpressionRoutedEvent(object target, object? expressionPayload, int? timeoutMs) =>
		InvokeTargetAdapter(target, adapter => adapter.RaiseExpressionRoutedEvent(target, expressionPayload, timeoutMs), "routed event expression");

	private static ActionResult RunKnownOperation(object target, string operation) =>
		InvokeTargetAdapter(target, adapter => adapter.RunKnownOperation(target, operation), $"known operation '{operation}'");

	private static (object? Target, StandardIpcResponse? Response) ResolveDragTarget(string commandName, string targetId, TreeService treeService, string role)
	{
		if (string.IsNullOrWhiteSpace(targetId))
			return (null, StandardIpcResponse.FromError($"{commandName}: a {role} target ID is required.", ProtocolConstants.ErrorCodes.InvalidArguments, PayloadLog.CurrentCorrelationId));

		var resolution = treeService.ResolveTarget(targetId);
		if (resolution.Status == TargetIdResolutionStatus.Found)
			return (resolution.Target!, null);

		var errorCode = resolution.Status == TargetIdResolutionStatus.Stale
			? ProtocolConstants.ErrorCodes.StaleTarget
			: ProtocolConstants.ErrorCodes.UnsupportedTarget;
		return (null, StandardIpcResponse.FromError($"{commandName}: {role} target '{targetId}' resolved as {resolution.Status}.", errorCode, PayloadLog.CurrentCorrelationId));
	}

	private static string? ValidateDragAndDropRequest(DragAndDropCommandRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.TargetId))
			return "Drag and drop requires a source target ID.";
		if (string.IsNullOrWhiteSpace(request.DestinationTargetId))
			return "Drag and drop requires a destination target ID.";
		if (request.DurationMs is < 0 or > 15_000)
			return "Drag duration must be between 0 and 15000 ms.";
		if (request.HoldMs is < 0 or > 5_000)
			return "Drag hold time must be between 0 and 5000 ms.";
		if (request.StepIntervalMs is < 1 or > 250)
			return "Drag step interval must be between 1 and 250 ms.";
		if (request.PostDropWaitMs is < 0 or > 5_000)
			return "Post-drop wait must be between 0 and 5000 ms.";
		if (!IsValidAnchor(request.SourceAnchorX) || !IsValidAnchor(request.SourceAnchorY) ||
			!IsValidAnchor(request.DestinationAnchorX) || !IsValidAnchor(request.DestinationAnchorY))
		{
			return "Drag anchors must be finite values between 0.0 and 1.0.";
		}

		return null;
	}

	private static bool IsValidAnchor(double value) =>
		value >= 0 && value <= 1 && !double.IsNaN(value) && !double.IsInfinity(value);

	private sealed class DeferredDragAndDropAction(DragPlan plan, string sourceTargetId) : IDeferredCommandAction
	{
		public Task<object> ExecuteAsync(CancellationToken cancellationToken)
		{
			using var syntheticInput = AppHooks.BeginSyntheticInput();
			var result = TargetMouseInput.PerformDragAndDrop(plan, cancellationToken);
			return Task.FromResult<object>(ToResponse(result, ProtocolConstants.Commands.DragAndDrop, sourceTargetId));
		}
	}
}
