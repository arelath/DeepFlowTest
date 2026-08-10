namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Reflection;
using System.Threading.Tasks;

internal static class TargetMethodInvoker
{
	public static object? AwaitTaskResult(object? result, int? timeoutMs)
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
