namespace DeepFlowTest.AppDriverPayload.Commands.TargetActions;

using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class ReflectionTargetActionHandler
{
	public static object Invoke(InvokeCommandRequest request, TreeService treeService)
	{
		if (!request.AllowUnsafeCode)
			return StandardIpcResponse.FromError("Invoke requires explicit unsafe-code opt-in.", ProtocolConstants.ErrorCodes.UnsupportedCommand, PayloadLog.CurrentCorrelationId);

		return TargetActionPipeline.Execute(ProtocolConstants.Commands.Invoke, request.TargetId, treeService, target =>
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
}
