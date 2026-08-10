namespace DeepFlowTest.Interop.Expressions.Visitors;

using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

internal sealed class SyncOverAsyncGuard : ExpressionVisitor
{
	public static void ThrowIfUnsafe(Expression expression)
	{
		new SyncOverAsyncGuard().Visit(expression);
	}

	protected override Expression VisitMember(MemberExpression node)
	{
		if (node.Member.Name == nameof(Task<object>.Result) && IsTaskType(node.Expression?.Type))
		{
			throw new InvalidOperationException(@"Task.Result will cause a deadlock.
Use the async version of the given method.
EG Invoke(() => FooAsync().Result) -> InvokeAsync(() => FooAsync())");
		}

		return base.VisitMember(node);
	}

	protected override Expression VisitMethodCall(MethodCallExpression node)
	{
		if (node.Method.Name == nameof(Task.GetAwaiter) && IsTaskType(node.Method.DeclaringType))
		{
			throw new InvalidOperationException(@"GetAwaiter() will cause a deadlock.
Use the async version of the given method.
EG Invoke(() => FooAsync().GetAwaiter().GetResult()) -> InvokeAsync(() => FooAsync())");
		}

		if (node.Method.Name == nameof(Task.Wait) && IsTaskType(node.Method.DeclaringType))
		{
			throw new InvalidOperationException(@"Wait() will cause a deadlock.
Use the async version of the given method.
EG Invoke(() => FooAsync().Wait()) -> InvokeAsync(() => FooAsync())");
		}

		return base.VisitMethodCall(node);
	}

	private static bool IsTaskType(Type? type) =>
		type is not null && typeof(Task).IsAssignableFrom(type);
}
