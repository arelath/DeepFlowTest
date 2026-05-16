namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
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

	public static object Focus(FocusCommandRequest request, TreeService treeService) =>
		WithTarget(ProtocolConstants.Commands.Focus, request.TargetId, treeService, target =>
			InvokeTargetAdapter(target, adapter => adapter.Focus(target), "focus"));

	public static object TypeText(TypeTextCommandRequest request, TreeService treeService)
	{
		if (!string.IsNullOrWhiteSpace(request.TargetId))
			return WithTarget(ProtocolConstants.Commands.TypeText, request.TargetId!, treeService, target => TypeTextIntoTarget(target, request.Text, request.ClearFirst));

		return ToResponse(TypeTextIntoFocusedTarget(request.Text, request.ClearFirst));
	}

	public static object KeyPress(KeyPressCommandRequest request, TreeService treeService)
	{
		if (!string.IsNullOrWhiteSpace(request.TargetId))
			return WithTarget(ProtocolConstants.Commands.KeyPress, request.TargetId!, treeService, target => SendKeysToTarget(target, request.Keys, request.DelayMs, request.EnsureForeground));

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

		if (ensureForeground)
			EnsureForegroundTarget(target);

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
}
