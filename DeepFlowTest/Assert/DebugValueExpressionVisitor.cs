namespace DeepFlowTest.Assert;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Newtonsoft.Json;

internal sealed class DebugValueExpressionVisitor : ExpressionStringBuilder
{
	public static ParameterExpression GetDebugExpression(string typeName, Element element) =>
		Expression.Parameter(typeof(Element), string.IsNullOrWhiteSpace(typeName) ? "element" : typeName);

	public static (string Body, IReadOnlyCollection<(string Name, object? Value)> Values) GetDiagnosticString(Expression body, Element element)
	{
		var propertyNames = ElementPropertyAccessCollector.Collect(body);
		var values = new List<(string Name, object? Value)>
		{
			(nameof(Element.TargetId), element.TargetId),
			(nameof(Element.TypeName), element.TypeName),
		};

		if (propertyNames.Count == 0)
		{
			values.Add(("Properties", JsonConvert.SerializeObject(element.Properties.OrderBy(static property => property.Key, StringComparer.Ordinal))));
		}
		else
		{
			foreach (var name in propertyNames.OrderBy(static name => name, StringComparer.Ordinal))
				values.Add((name, element.Properties.TryGetValue(name, out var value) ? value : Primitive.Empty));
		}

		return (ExpressionStringBuilder.ToString(body), values);
	}

	private sealed class ElementPropertyAccessCollector : ExpressionVisitor
	{
		private readonly HashSet<string> propertyNames = new(StringComparer.Ordinal);

		public static IReadOnlyCollection<string> Collect(Expression expression)
		{
			var collector = new ElementPropertyAccessCollector();
			collector.Visit(expression);
			return collector.propertyNames;
		}

		protected override Expression VisitIndex(IndexExpression node)
		{
			if (IsElementExpression(node.Object) && node.Arguments.Count == 1 && TryGetString(node.Arguments[0], out var propertyName))
				propertyNames.Add(propertyName);

			return base.VisitIndex(node);
		}

		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			if (IsElementExpression(node.Object)
				&& node.Arguments.Count == 1
				&& (node.Method.Name == "get_Item" || node.Method.Name == nameof(Element.HasProperty))
				&& TryGetString(node.Arguments[0], out var propertyName))
			{
				propertyNames.Add(propertyName);
			}

			return base.VisitMethodCall(node);
		}

		private static bool IsElementExpression(Expression? expression) =>
			expression is not null && typeof(Element).IsAssignableFrom(expression.Type);

		private static bool TryGetString(Expression expression, out string value)
		{
			while (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert)
				expression = convert.Operand;

			switch (expression)
			{
				case ConstantExpression { Value: string constant }:
					value = constant;
					return true;
				case MemberExpression memberExpression:
					var converted = Expression.Convert(memberExpression, typeof(object));
					var lambda = Expression.Lambda<Func<object?>>(converted);
					value = Convert.ToString(lambda.Compile().Invoke(), CultureInfo.InvariantCulture) ?? string.Empty;
					return !string.IsNullOrWhiteSpace(value);
				default:
					value = string.Empty;
					return false;
			}
		}
	}
}
