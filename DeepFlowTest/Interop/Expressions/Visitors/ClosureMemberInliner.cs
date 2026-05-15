namespace DeepFlowTest.Interop.Expressions.Visitors;

using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

internal sealed class ClosureMemberInliner : ExpressionVisitor
{
	protected override Expression VisitMember(MemberExpression node)
	{
		if (TryEvaluateClosureChain(node, out var value))
			return Expression.Constant(value, node.Type);

		return base.VisitMember(node);
	}

	private static bool TryEvaluateClosureChain(MemberExpression node, out object? value)
	{
		var members = new Stack<MemberInfo>();
		Expression? current = node;
		while (current is MemberExpression memberExpression)
		{
			members.Push(memberExpression.Member);
			current = memberExpression.Expression;
		}

		value = null;
		if (current is not ConstantExpression constantExpression)
			return false;

		object? container = constantExpression.Value;
		if (container is null)
			return true;

		while (members.Count != 0)
		{
			if (container is null)
			{
				value = null;
				return true;
			}

			var member = members.Pop();
			switch (member)
			{
				case FieldInfo field:
					container = field.GetValue(container);
					break;
				case PropertyInfo property when property.GetIndexParameters().Length == 0:
					container = property.GetValue(container, null);
					break;
				default:
					return false;
			}
		}

		value = container;
		return true;
	}
}
