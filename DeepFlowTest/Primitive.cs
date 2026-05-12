namespace DeepFlowTest;

using System;
using System.Globalization;

public sealed class Primitive : IEquatable<Primitive>, IEquatable<string>, IEquatable<double>, IEquatable<float>, IEquatable<int>, IEquatable<long>, IEquatable<bool>
{
	public static readonly Primitive Empty = new(null);

	public Primitive(object? value, string? targetId = null, string? propertyName = null)
	{
		Value = value;
		TargetId = targetId;
		PropertyName = propertyName;
	}

	public object? Value { get; }

	public string? TargetId { get; }

	public string? PropertyName { get; }

	public T? As<T>()
	{
		if (Value is null)
			return default;

		if (Value is T typed)
			return typed;

		var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
		if (targetType.IsEnum)
			return (T)Enum.Parse(targetType, Convert.ToString(Value, CultureInfo.InvariantCulture) ?? string.Empty, ignoreCase: true);

		return (T?)Convert.ChangeType(Value, targetType, CultureInfo.InvariantCulture);
	}

	public T? To<T>() => As<T>();

	public string S => ToString();

	public override string ToString() => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? string.Empty;

	public override bool Equals(object? obj) =>
		obj is Primitive primitive
			? ValuesEqual(Value, primitive.Value)
			: ValuesEqual(Value, obj);

	public bool Equals(Primitive? other) => ValuesEqual(Value, other?.Value);

	public bool Equals(string? other) => ValuesEqual(Value, other);

	public bool Equals(double other) => ValuesEqual(Value, other);

	public bool Equals(float other) => ValuesEqual(Value, other);

	public bool Equals(int other) => ValuesEqual(Value, other);

	public bool Equals(long other) => ValuesEqual(Value, other);

	public bool Equals(bool other) => ValuesEqual(Value, other);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public static Primitive FromProperty(Element element, string propertyName)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		element.Properties.TryGetValue(propertyName, out var value);
		return new Primitive(value, element.TargetId, propertyName);
	}

	public static implicit operator Primitive(string? value) => new(value);
	public static implicit operator Primitive(bool value) => new(value);
	public static implicit operator Primitive(bool? value) => new(value);
	public static implicit operator Primitive(int value) => new(value);
	public static implicit operator Primitive(int? value) => new(value);
	public static implicit operator Primitive(long value) => new(value);
	public static implicit operator Primitive(long? value) => new(value);
	public static implicit operator Primitive(float value) => new(value);
	public static implicit operator Primitive(float? value) => new(value);
	public static implicit operator Primitive(double value) => new(value);
	public static implicit operator Primitive(double? value) => new(value);
	public static implicit operator Primitive(decimal value) => new(value);
	public static implicit operator Primitive(decimal? value) => new(value);

	public static implicit operator string?(Primitive primitive) => primitive.As<string>();
	public static implicit operator bool(Primitive primitive) => primitive.As<bool>();
	public static implicit operator bool?(Primitive primitive) => primitive.As<bool?>();
	public static implicit operator int(Primitive primitive) => primitive.As<int>();
	public static implicit operator int?(Primitive primitive) => primitive.As<int?>();
	public static implicit operator long(Primitive primitive) => primitive.As<long>();
	public static implicit operator long?(Primitive primitive) => primitive.As<long?>();
	public static implicit operator float(Primitive primitive) => primitive.As<float>();
	public static implicit operator float?(Primitive primitive) => primitive.As<float?>();
	public static implicit operator double(Primitive primitive) => primitive.As<double>();
	public static implicit operator double?(Primitive primitive) => primitive.As<double?>();
	public static implicit operator decimal(Primitive primitive) => primitive.As<decimal>();
	public static implicit operator decimal?(Primitive primitive) => primitive.As<decimal?>();

	public static bool operator ==(Primitive? left, Primitive? right) => ValuesEqual(left?.Value, right?.Value);
	public static bool operator !=(Primitive? left, Primitive? right) => !ValuesEqual(left?.Value, right?.Value);
	public static bool operator ==(Primitive? left, string? right) => ValuesEqual(left?.Value, right);
	public static bool operator !=(Primitive? left, string? right) => !ValuesEqual(left?.Value, right);
	public static bool operator ==(string? left, Primitive? right) => ValuesEqual(left, right?.Value);
	public static bool operator !=(string? left, Primitive? right) => !ValuesEqual(left, right?.Value);
	public static bool operator ==(Primitive? left, bool right) => ValuesEqual(left?.Value, right);
	public static bool operator !=(Primitive? left, bool right) => !ValuesEqual(left?.Value, right);
	public static bool operator ==(bool left, Primitive? right) => ValuesEqual(left, right?.Value);
	public static bool operator !=(bool left, Primitive? right) => !ValuesEqual(left, right?.Value);

	public static bool operator >(Primitive left, Primitive right) => Compare(left.Value, right.Value) > 0;
	public static bool operator <(Primitive left, Primitive right) => Compare(left.Value, right.Value) < 0;
	public static bool operator >=(Primitive left, Primitive right) => Compare(left.Value, right.Value) >= 0;
	public static bool operator <=(Primitive left, Primitive right) => Compare(left.Value, right.Value) <= 0;

	public static Primitive operator +(Primitive left, Primitive right)
	{
		dynamic leftValue = left.Value!;
		dynamic rightValue = right.Value!;
		return new Primitive(leftValue + rightValue);
	}

	public static Primitive operator -(Primitive left, Primitive right)
	{
		dynamic leftValue = left.Value!;
		dynamic rightValue = right.Value!;
		return new Primitive(leftValue - rightValue);
	}

	public static Primitive operator *(Primitive left, Primitive right)
	{
		dynamic leftValue = left.Value!;
		dynamic rightValue = right.Value!;
		return new Primitive(leftValue * rightValue);
	}

	public static Primitive operator /(Primitive left, Primitive right)
	{
		dynamic leftValue = left.Value!;
		dynamic rightValue = right.Value!;
		return new Primitive(leftValue / rightValue);
	}

	public static Primitive operator %(Primitive left, Primitive right)
	{
		dynamic leftValue = left.Value!;
		dynamic rightValue = right.Value!;
		return new Primitive(leftValue % rightValue);
	}

	public static Primitive operator ^(Primitive left, Primitive right)
	{
		dynamic leftValue = left.Value!;
		dynamic rightValue = right.Value!;
		return new Primitive(leftValue ^ rightValue);
	}

	public static Primitive operator <<(Primitive left, int right)
	{
		dynamic leftValue = left.Value!;
		return new Primitive(leftValue << right);
	}

	public static Primitive operator >>(Primitive left, int right)
	{
		dynamic leftValue = left.Value!;
		return new Primitive(leftValue >> right);
	}

	public static Primitive operator ~(Primitive primitive)
	{
		dynamic value = primitive.Value!;
		return new Primitive(~value);
	}

	public static Primitive operator ++(Primitive primitive)
	{
		dynamic value = primitive.Value!;
		return new Primitive(++value);
	}

	public static Primitive operator --(Primitive primitive)
	{
		dynamic value = primitive.Value!;
		return new Primitive(--value);
	}

	public static Primitive operator +(Primitive primitive)
	{
		dynamic value = primitive.Value!;
		return new Primitive(+value);
	}

	public static Primitive operator -(Primitive primitive)
	{
		dynamic value = primitive.Value!;
		return new Primitive(-value);
	}

	public static bool operator true(Primitive primitive) => primitive.As<bool>();
	public static bool operator false(Primitive primitive) => !primitive.As<bool>();
	public static Primitive operator !(Primitive primitive) => new(!primitive.As<bool>());
	public static Primitive operator &(Primitive left, Primitive right)
	{
		dynamic leftValue = left.Value!;
		dynamic rightValue = right.Value!;
		return new Primitive(leftValue & rightValue);
	}

	public static Primitive operator |(Primitive left, Primitive right)
	{
		dynamic leftValue = left.Value!;
		dynamic rightValue = right.Value!;
		return new Primitive(leftValue | rightValue);
	}

	private static bool ValuesEqual(object? left, object? right)
	{
		if (left is null || right is null)
			return left is null && right is null;

		if (TryToDecimal(left, out var leftNumber) && TryToDecimal(right, out var rightNumber))
			return leftNumber == rightNumber;

		return string.Equals(
			Convert.ToString(left, CultureInfo.InvariantCulture),
			Convert.ToString(right, CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
	}

	private static int Compare(object? left, object? right) => ToDecimal(left).CompareTo(ToDecimal(right));

	private static decimal ToDecimal(object? value)
	{
		if (TryToDecimal(value, out var number))
			return number;

		throw new InvalidOperationException($"Value '{value}' is not numeric.");
	}

	private static bool TryToDecimal(object? value, out decimal number)
	{
		switch (value)
		{
			case null:
				number = 0;
				return false;
			case decimal decimalValue:
				number = decimalValue;
				return true;
			case IConvertible convertible:
				try
				{
					number = convertible.ToDecimal(CultureInfo.InvariantCulture);
					return true;
				}
				catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
				{
					break;
				}
		}

		number = 0;
		return false;
	}
}
