namespace DeepFlowTest.Interop;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using DeepFlowTest;
using DeepFlowTest.Interop.Expressions;
using DeepFlowTest.Interop.Expressions.Visitors;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serialize.Linq.Serializers;

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

	private static readonly ExpressionSerializer Serializer = new DeepFlowTestSerializeLinqSerializer();
}
