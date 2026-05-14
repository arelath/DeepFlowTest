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
using System.Threading.Tasks;
using DeepFlowTest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serialize.Linq.Factories;
using Serialize.Linq.Interfaces;
using Serialize.Linq.Serializers;
using SerializeJsonSerializer = Serialize.Linq.Serializers.JsonSerializer;

public static class ExpressionPayloadSerializer
{
	public static ExpressionMatcherPayload Serialize<TDelegate>(Expression<TDelegate> expression)
	{
		_ = expression ?? throw new ArgumentNullException(nameof(expression));

		var closureValues = ClosureValueCollector.Collect(expression);
		var expressionText = expression.ToString();
		var expressionJson = SerializeText(expression);
		var canonicalPayload = CreateCanonicalPayload(expression, expressionText, expressionJson, closureValues);

		return new ExpressionMatcherPayload
		{
			ExpressionJson = expressionJson,
			ExpressionText = expressionText,
			ExpressionHash = ComputeSha256(canonicalPayload),
			ClosureValues = closureValues,
		};
	}

	public static string SerializeText(LambdaExpression expression)
	{
		_ = expression ?? throw new ArgumentNullException(nameof(expression));
		if (!ExpressionPayloadOptions.AllowUnsafeSyncOverAsync)
			SyncOverAsyncGuard.ThrowIfUnsafe(expression);

		// Closures (compiler-generated DisplayClass instances) only exist in the test process
		// — they don't survive deserialization on the payload side. Inline any MemberExpression
		// chain rooted in a ConstantExpression (the closure object) into a ConstantExpression
		// so the serialized matcher carries the captured value rather than a reference into a
		// type the payload can't load.
		var closureInlined = (LambdaExpression)new ClosureMemberInliner().Visit(expression)!;
		var serializableExpression = (LambdaExpression)new EnumConstantRewriter().Visit(closureInlined)!;
		return Serializer.SerializeText(serializableExpression);
	}

	public static string FormatDiagnosticText(LambdaExpression expression)
	{
		_ = expression ?? throw new ArgumentNullException(nameof(expression));
		try
		{
			var closureInlined = (LambdaExpression)new ClosureMemberInliner().Visit(expression)!;
			return closureInlined.ToString();
		}
		catch
		{
			return expression.ToString();
		}
	}

	public static LambdaExpression Deserialize(string expressionJson)
	{
		if (string.IsNullOrWhiteSpace(expressionJson))
			throw new InvalidOperationException("Expression payload is empty.");

		return (LambdaExpression)Serializer.DeserializeText(expressionJson);
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

	private static readonly FactorySettings Settings = new()
	{
		AllowPrivateFieldAccess = true,
	};

	private static readonly ExpressionSerializer Serializer = new DeepFlowTestSerializeLinqSerializer();

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

	private sealed class ClosureMemberInliner : ExpressionVisitor
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

	private sealed class EnumConstantRewriter : ExpressionVisitor
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

	private sealed class DeepFlowTestSerializeLinqSerializer : ExpressionSerializer
	{
		public DeepFlowTestSerializeLinqSerializer()
			: base(new SerializeJsonSerializer(), Settings)
		{
		}

		protected override INodeFactory CreateFactory(Expression expression, FactorySettings factorySettings)
		{
			var expectedTypes = ExpressionExpectedTypeCollector.Collect(expression);
			if (expression is LambdaExpression lambda)
				expectedTypes.AddRange(lambda.Parameters.Select(static parameter => parameter.Type));

			return new DefaultNodeFactory(expectedTypes, factorySettings);
		}
	}

	private sealed class ExpressionExpectedTypeCollector : ExpressionVisitor
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

	private sealed class SyncOverAsyncGuard : ExpressionVisitor
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
}
