namespace DeepFlowTest.Interop.Expressions.Visitors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

internal sealed class ExpressionExpectedTypeCollector : ExpressionVisitor
{
	private readonly HashSet<Type> expectedTypes = new();

	public static List<Type> Collect(Expression expression)
	{
		var collector = new ExpressionExpectedTypeCollector();
		collector.Visit(expression);
		return collector.expectedTypes.ToList();
	}

	protected override Expression VisitMember(MemberExpression node)
	{
		AddExpectedType(node.Member.DeclaringType);
		return base.VisitMember(node);
	}

	protected override Expression VisitConstant(ConstantExpression node)
	{
		AddExpectedType(node.Value?.GetType());
		return base.VisitConstant(node);
	}

	private void AddExpectedType(Type? type)
	{
		if (type is null || IsCompilerGeneratedClosure(type))
			return;

		expectedTypes.Add(type);
	}

	private static bool IsCompilerGeneratedClosure(Type type) =>
		type.Name.IndexOf("<>c__", StringComparison.Ordinal) >= 0;
}
