namespace DeepFlowTest.Assert;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

internal static class ElementAssertExtensions
{
	public static string GetDiagnosticMessage(Expression body, Element element, Expression valueExpression, Exception? exception)
	{
		var (bodyString, bodyValues) = DebugValueExpressionVisitor.GetDiagnosticString(body, element);
		return GetDiagnosticMessage(bodyString, bodyValues, exception);
	}

	public static string GetDiagnosticMessage(string expected, IReadOnlyCollection<(string Name, object? Value)> actualValues, Exception? exception)
	{
		var builder = new StringBuilder()
			.AppendLine()
			.AppendLine("Expected:")
			.Append('\t')
			.AppendLine(expected);

		if (actualValues.Any())
		{
			builder.AppendLine()
				.AppendLine("Actual:");
			foreach (var (name, value) in actualValues)
				builder.Append('\t').Append(name).Append(" == ").AppendLine(FormatValue(value));
		}

		if (exception is not null)
		{
			builder.AppendLine()
				.Append(exception.GetType().Name)
				.Append(": ")
				.AppendLine(exception.Message);
		}

		return builder.ToString();
	}

	private static string FormatValue(object? value)
	{
		return value switch
		{
			null => "null",
			bool boolValue => boolValue ? "true" : "false",
			string stringValue => "\"" + stringValue + "\"",
			Primitive primitive => primitive.S,
			_ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
		};
	}
}
