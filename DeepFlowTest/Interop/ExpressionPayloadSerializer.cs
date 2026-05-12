namespace DeepFlowTest.Interop;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serialize.Linq.Extensions;
using Serialize.Linq.Factories;

public static class ExpressionPayloadSerializer
{
	public static ExpressionMatcherPayload Serialize<TDelegate>(Expression<TDelegate> expression)
	{
		_ = expression ?? throw new ArgumentNullException(nameof(expression));

		var closureValues = ClosureValueCollector.Collect(expression);
		var expressionText = expression.ToString();
		var expressionJson = expression.ToJson(new FactorySettings { AllowPrivateFieldAccess = true });
		var canonicalPayload = CreateCanonicalPayload(expression, expressionText, expressionJson, closureValues);

		return new ExpressionMatcherPayload
		{
			ExpressionJson = expressionJson,
			ExpressionText = expressionText,
			ExpressionHash = ComputeSha256(canonicalPayload),
			ClosureValues = closureValues,
		};
	}

	private static string CreateCanonicalPayload(LambdaExpression expression, string expressionText, string expressionJson, Dictionary<string, object?> closureValues)
	{
		var payload = new JObject
		{
			["ExpressionJson"] = expressionJson,
			["ExpressionText"] = expressionText,
			["ReturnType"] = expression.ReturnType.AssemblyQualifiedName,
			["ParameterTypes"] = new JArray(expression.Parameters.Select(static parameter => parameter.Type.AssemblyQualifiedName)),
			["ClosureValues"] = ToCanonicalJObject(closureValues),
		};

		return payload.ToString(Formatting.None);
	}

	private static JObject ToCanonicalJObject(Dictionary<string, object?> values)
	{
		var result = new JObject();
		foreach (var item in values.OrderBy(static item => item.Key, StringComparer.Ordinal))
			result[item.Key] = item.Value is null ? JValue.CreateNull() : JToken.FromObject(item.Value);
		return result;
	}

	private static string ComputeSha256(string text)
	{
		using var sha256 = SHA256.Create();
		var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
		return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
	}

	private static object? NormalizeValue(object? value)
	{
		if (value is null)
			return null;

		var valueType = value.GetType();
		if (value is string
			|| value is bool
			|| value is byte
			|| value is sbyte
			|| value is short
			|| value is ushort
			|| value is int
			|| value is uint
			|| value is long
			|| value is ulong
			|| value is float
			|| value is double
			|| value is decimal)
		{
			return value;
		}

		if (value is char character)
			return character.ToString();

		if (valueType.IsEnum)
			return value.ToString();

		if (value is DateTime dateTime)
			return dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

		if (value is DateTimeOffset dateTimeOffset)
			return dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

		if (value is Array array)
		{
			var normalized = new List<object?>(array.Length);
			foreach (var item in array)
				normalized.Add(NormalizeValue(item));
			return normalized;
		}

		if (value is IDictionary dictionary)
		{
			var normalized = new SortedDictionary<string, object?>(StringComparer.Ordinal);
			foreach (var key in dictionary.Keys)
			{
				if (key is null)
					continue;

				normalized[Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty] = NormalizeValue(dictionary[key]);
			}

			return normalized;
		}

		return value.ToString();
	}

	private sealed class ClosureValueCollector : ExpressionVisitor
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
				values[key] = NormalizeValue(value);

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
}
