namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal static class TargetExpressionEvaluator
{
	public static bool CanEvaluate(object? rawPayload) =>
		TryGetExpressionPayload(rawPayload, out _);

	public static object? Evaluate(object target, object? rawPayload, int? timeoutMs, bool awaitTasks)
	{
		if (!TryGetExpressionDelegate(rawPayload, out var expression))
			return TargetValueConverter.UnwrapJsonValue(rawPayload);

		var result = expression.DynamicInvoke(target);
		return awaitTasks ? AwaitTaskResult(result, timeoutMs) : result;
	}

	private static bool TryGetExpressionDelegate(object? rawPayload, out Delegate expression)
	{
		expression = null!;
		if (rawPayload is null)
			return false;

		var mapped = ArgsMapper.MapSingle(rawPayload);
		if (mapped is Delegate mappedDelegate)
		{
			expression = mappedDelegate;
			return true;
		}

		if (!TryGetExpressionPayload(rawPayload, out var payload))
			return false;

		expression = DeserializeExpression(payload).Compile();
		return true;
	}

	private static bool TryGetExpressionPayload(object? rawPayload, out ExpressionMatcherPayload payload)
	{
		payload = null!;
		if (rawPayload is null)
			return false;

		try
		{
			payload = MessagePacker.ConvertTo<ExpressionMatcherPayload>(rawPayload);
			return !string.IsNullOrWhiteSpace(payload.ExpressionJson);
		}
		catch (Exception ex) when (ex is InvalidCastException or ArgumentException or ProtocolException or Newtonsoft.Json.JsonException)
		{
			return false;
		}
	}

	private static LambdaExpression DeserializeExpression(ExpressionMatcherPayload payload)
	{
		if (string.IsNullOrWhiteSpace(payload.ExpressionJson))
			throw new InvalidOperationException("Expression payload is empty.");

		return ExpressionPayloadSerializer.Deserialize(payload.ExpressionJson);
	}

	private static object? AwaitTaskResult(object? result, int? timeoutMs)
	{
		if (result is not Task task)
			return result;

		if (timeoutMs is > 0)
		{
			if (!task.Wait(timeoutMs.Value))
				throw new TimeoutException("Expression task did not complete within the command timeout.");
		}
		else
		{
			task.GetAwaiter().GetResult();
		}

		var resultProperty = task.GetType().GetProperty(nameof(Task<object>.Result), BindingFlags.Instance | BindingFlags.Public);
		return resultProperty?.GetValue(task, null);
	}
}
