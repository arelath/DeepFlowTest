namespace DeepFlowTest.Interop;

using System;

public sealed class WrappedArg<T>
{
	public static object? Wrap(object? item)
	{
		if (item is null)
			return null;

		var wrappedType = typeof(WrappedArg<>).MakeGenericType(item.GetType());
		var wrappedItem = Activator.CreateInstance(wrappedType)!;
		var property = wrappedType.GetProperty(nameof(Value))!;
		property.SetValue(wrappedItem, item, null);
		return wrappedItem;
	}

	public T? Value { get; set; }

	public string Type { get; } = WrappedArgType;

	public const string WrappedArgType = "p:WrappedArg";
}
