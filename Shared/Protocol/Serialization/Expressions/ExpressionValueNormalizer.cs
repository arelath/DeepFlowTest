namespace DeepFlowTest.Interop.Expressions;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

internal static class ExpressionValueNormalizer
{
	public static object? Normalize(object? value)
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
				normalized.Add(Normalize(item));
			return normalized;
		}

		if (value is IDictionary dictionary)
		{
			var normalized = new SortedDictionary<string, object?>(StringComparer.Ordinal);
			foreach (var key in dictionary.Keys)
			{
				if (key is null)
					continue;

				normalized[Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty] = Normalize(dictionary[key]);
			}

			return normalized;
		}

		return value.ToString();
	}
}
