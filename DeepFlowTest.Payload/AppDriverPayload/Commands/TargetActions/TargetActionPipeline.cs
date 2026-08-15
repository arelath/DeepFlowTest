namespace DeepFlowTest.AppDriverPayload.Commands.TargetActions;

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class TargetActionPipeline
{
	public static object Execute(
		string commandName,
		string targetId,
		TreeService treeService,
		Func<object, ActionResult> invoke) =>
		Execute(commandName, treeService, [TargetActionTarget.Primary(targetId)], targets => TargetActionOutcome.Completed(invoke(targets[0])));

	public static object Execute(
		string commandName,
		TreeService treeService,
		IReadOnlyList<TargetActionTarget> targetRequests,
		Func<IReadOnlyList<object>, TargetActionOutcome> invoke)
	{
		var targets = new object[targetRequests.Count];
		for (var i = 0; i < targetRequests.Count; i++)
		{
			var targetRequest = targetRequests[i];
			var resolutionError = ResolveTarget(commandName, targetRequest, treeService, out var target);
			if (resolutionError is not null)
				return resolutionError;

			targets[i] = target!;
		}

		try
		{
			using var syntheticInput = AppHooks.BeginSyntheticInput();
			var outcome = invoke(targets);
			if (outcome.IsDeferred)
				return new DeferredTargetAction(commandName, targetRequests[0].TargetId, outcome.Deferred!);

			return ToResponse(outcome.Result, commandName, targetRequests[0].TargetId);
		}
		catch (Exception ex) when (CanTranslate(ex))
		{
			return TranslateException(ex, commandName, targetRequests[0].TargetId);
		}
	}

	public static object ExecuteUntargeted(Func<ActionResult> invoke)
	{
		using var syntheticInput = AppHooks.BeginSyntheticInput();
		return ToResponse(invoke());
	}

	internal static StandardIpcResponse ToResponse(ActionResult result, string? commandName = null, string? targetId = null)
	{
		if (result.Success)
			return SerializableSuccess(result.Value);

		var error = result.Error ?? "The requested action is not supported for this target.";
		if (result.FormatErrorContext)
			error = FormatActionError(error, commandName, targetId);

		return StandardIpcResponse.FromError(
			error,
			result.ErrorCode ?? ProtocolConstants.ErrorCodes.UnsupportedTarget,
			PayloadLog.CurrentCorrelationId);
	}

	private static StandardIpcResponse? ResolveTarget(
		string commandName,
		TargetActionTarget targetRequest,
		TreeService treeService,
		out object? target)
	{
		target = null;
		if (string.IsNullOrWhiteSpace(targetRequest.TargetId))
		{
			var message = targetRequest.Role is null
				? $"{commandName}: a target ID is required."
				: $"{commandName}: a {targetRequest.Role} target ID is required.";
			return StandardIpcResponse.FromError(message, targetRequest.MissingTargetErrorCode, PayloadLog.CurrentCorrelationId);
		}

		var resolution = treeService.ResolveTarget(targetRequest.TargetId);
		if (resolution.Status == TargetIdResolutionStatus.Found)
		{
			target = resolution.Target!;
			return null;
		}

		var errorCode = resolution.Status == TargetIdResolutionStatus.Stale
			? ProtocolConstants.ErrorCodes.StaleTarget
			: ProtocolConstants.ErrorCodes.UnsupportedTarget;
		var error = targetRequest.Role is null
			? $"{commandName}: target '{targetRequest.TargetId}' resolved as {resolution.Status}."
			: $"{commandName}: {targetRequest.Role} target '{targetRequest.TargetId}' resolved as {resolution.Status}.";
		return StandardIpcResponse.FromError(error, errorCode, PayloadLog.CurrentCorrelationId);
	}

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

	private static bool CanTranslate(Exception ex) =>
		ex is not OutOfMemoryException && ex is not StackOverflowException;

	private static StandardIpcResponse TranslateException(Exception ex, string commandName, string? targetId)
	{
		if (ex is TimeoutException)
		{
			return StandardIpcResponse.FromError(
				$"{commandName}: action timed out for target '{targetId ?? string.Empty}': {ex.Message}",
				ProtocolConstants.ErrorCodes.CommandTimeout,
				PayloadLog.CurrentCorrelationId);
		}

		if (ex is SerializationException)
		{
			return StandardIpcResponse.FromError(
				$"{commandName}: action result is not serializable for target '{targetId ?? string.Empty}': {ex.Message}",
				ProtocolConstants.ErrorCodes.ProtocolError,
				PayloadLog.CurrentCorrelationId);
		}

		return StandardIpcResponse.FromError(
			$"{commandName}: action failed for target '{targetId ?? string.Empty}': {ex.Message}",
			ProtocolConstants.ErrorCodes.UnsupportedTarget,
			PayloadLog.CurrentCorrelationId);
	}

	private static string FormatActionError(string error, string? commandName, string? targetId)
	{
		if (string.IsNullOrWhiteSpace(commandName) && string.IsNullOrWhiteSpace(targetId))
			return error;

		return $"{commandName ?? "action"}: target '{targetId ?? string.Empty}': {error}";
	}

	private sealed class DeferredTargetAction(
		string commandName,
		string targetId,
		Func<CancellationToken, ActionResult> execute) : IDeferredCommandAction
	{
		public Task<object> ExecuteAsync(CancellationToken cancellationToken)
		{
			try
			{
				using var syntheticInput = AppHooks.BeginSyntheticInput();
				return Task.FromResult<object>(ToResponse(execute(cancellationToken), commandName, targetId));
			}
			catch (Exception ex) when (CanTranslate(ex))
			{
				return Task.FromResult<object>(TranslateException(ex, commandName, targetId));
			}
		}
	}
}

internal readonly struct TargetActionTarget
{
	private TargetActionTarget(string targetId, string? role, string missingTargetErrorCode)
	{
		TargetId = targetId;
		Role = role;
		MissingTargetErrorCode = missingTargetErrorCode;
	}

	public string TargetId { get; }

	public string? Role { get; }

	public string MissingTargetErrorCode { get; }

	public static TargetActionTarget Primary(string targetId) =>
		new(targetId, null, ProtocolConstants.ErrorCodes.UnsupportedTarget);

	public static TargetActionTarget DragRole(string targetId, string role) =>
		new(targetId, role, ProtocolConstants.ErrorCodes.InvalidArguments);
}

internal readonly struct TargetActionOutcome
{
	private TargetActionOutcome(ActionResult result, Func<CancellationToken, ActionResult>? deferred)
	{
		Result = result;
		Deferred = deferred;
	}

	public ActionResult Result { get; }

	public Func<CancellationToken, ActionResult>? Deferred { get; }

	public bool IsDeferred => Deferred is not null;

	public static TargetActionOutcome Completed(ActionResult result) => new(result, null);

	public static TargetActionOutcome Defer(Func<CancellationToken, ActionResult> execute) => new(default, execute);
}
