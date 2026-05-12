namespace DeepFlowTest;

using System;
using System.Globalization;

public sealed class Primitive
{
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

		return (T?)Convert.ChangeType(Value, typeof(T), CultureInfo.InvariantCulture);
	}

	public override string ToString() => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? string.Empty;

	public static Primitive FromProperty(Element element, string propertyName)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		element.Properties.TryGetValue(propertyName, out var value);
		return new Primitive(value, element.TargetId, propertyName);
	}
}
