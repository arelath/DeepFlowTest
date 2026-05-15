namespace DeepFlowTest.Interop.Expressions.Visitors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DeepFlowTest.Interop.Expressions;

internal sealed class ClosureValueCollector : ExpressionVisitor
{
	private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);

	public static Dictionary<string, object?> Collect(Expression expression)
	{
		var collector = new ClosureValueCollector();
		collector.Visit(expression);
		return collector.values;
	}

	protected override Expression VisitMember(MemberExpression node)
	{
		if (TryEvaluateClosureMember(node, out var key, out var value))
			values[key] = ExpressionValueNormalizer.Normalize(value);

		return base.VisitMember(node);
	}

	private static bool TryEvaluateClosureMember(MemberExpression node, out string key, out object? value)
	{
		var members = new Stack<MemberInfo>();
		Expression? current = node;
		while (current is MemberExpression memberExpression)
		{
			members.Push(memberExpression.Member);
			current = memberExpression.Expression;
		}

		key = string.Join(".", members.Select(static member => member.Name));
		value = null;

		if (current is not ConstantExpression constantExpression)
			return false;

		var container = constantExpression.Value;
		if (container is null || key.Length == 0)
			return false;

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
