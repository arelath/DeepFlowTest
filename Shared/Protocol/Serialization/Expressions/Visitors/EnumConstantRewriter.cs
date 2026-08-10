namespace DeepFlowTest.Interop.Expressions.Visitors;

using System;
using System.Globalization;
using System.Linq.Expressions;

internal sealed class EnumConstantRewriter : ExpressionVisitor
{
	protected override Expression VisitConstant(ConstantExpression node)
	{
		if (node.Value is null || !node.Type.IsEnum)
			return base.VisitConstant(node);

		var underlyingType = Enum.GetUnderlyingType(node.Type);
		var underlyingValue = Convert.ChangeType(node.Value, underlyingType, CultureInfo.InvariantCulture);
		return Expression.Convert(Expression.Constant(underlyingValue, underlyingType), node.Type);
	}
}
