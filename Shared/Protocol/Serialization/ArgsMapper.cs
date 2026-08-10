namespace DeepFlowTest.Interop;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class ArgsMapper
{
	public static bool IsSerializable(object result)
	{
		try
		{
			var serialized = MessagePacker.Pack(WrappedArg<object>.Wrap(result) ?? new { Value = (object?)null });
			_ = MessagePacker.Unpack(serialized);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static dynamic?[] Map(IReadOnlyList<dynamic?> args) =>
		args.Select(MapSingle).ToArray();

	public static dynamic? MapSingle(dynamic? arg)
	{
		if (arg is null || !TryGetValue((object)arg, "Type", out var type))
			return arg;

		return type?.ToString() switch
		{
			Eval.EvalType => MapEvalArg(arg),
			WrappedArg<object>.WrappedArgType => MapArg(arg),
			_ => arg,
		};
	}

	private static dynamic MapEvalArg(dynamic arg)
	{
		if (!TryGetValue((object)arg, "ExpressionJson", out var expressionJson))
			throw new ArgumentException("Eval.ExpressionJson must be set.");
		if (expressionJson is not string expressionJsonString)
			throw new ArgumentException($"Eval.ExpressionJson must be a string. Received: {CreateLogString(expressionJson)} Type: {expressionJson?.GetType()}");

		var expression = ExpressionPayloadSerializer.Deserialize(expressionJsonString);
		return expression.Compile();
	}

	private static dynamic? MapArg(dynamic arg)
	{
		if (!TryGetValue((object)arg, "Value", out var value))
			throw new ArgumentException("Value must be set.");

		return value;
	}

	private static bool TryGetValue(object obj, string propertyName, out object? value)
	{
		if (obj is IDictionary<string, object?> genericDictionary)
			return genericDictionary.TryGetValue(propertyName, out value);

		if (obj is IDictionary dictionary)
		{
			foreach (var item in dictionary)
			{
				if (item is not DictionaryEntry entry)
					continue;

				if (string.Equals(entry.Key?.ToString(), propertyName, StringComparison.Ordinal))
				{
					value = entry.Value;
					return true;
				}
			}
		}

		if (obj is JObject jObject && jObject.TryGetValue(propertyName, StringComparison.Ordinal, out var token))
		{
			value = token.Type == JTokenType.Null ? null : token.ToObject<object?>();
			return true;
		}

		var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
		if (property is not null && property.GetIndexParameters().Length == 0)
		{
			value = property.GetValue(obj, null);
			return true;
		}

		value = null;
		return false;
	}

	private static string CreateLogString(object? item)
	{
		try
		{
			return JsonConvert.SerializeObject(item);
		}
		catch
		{
			return item?.ToString() ?? string.Empty;
		}
	}
}
