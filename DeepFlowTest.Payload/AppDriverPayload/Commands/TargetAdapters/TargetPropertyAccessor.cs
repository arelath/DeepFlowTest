namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Reflection;
using DeepFlowTest.AppDriverPayload.Commands;

internal static class TargetPropertyAccessor
{
	public static bool TrySetClrProperty(object target, string propertyName, object? value, out ActionResult result)
	{
		var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
		if (property is null)
		{
			result = default;
			return false;
		}

		if (!property.CanWrite || property.GetIndexParameters().Length != 0)
		{
			result = ActionResult.Unsupported($"Property '{propertyName}' is read-only.");
			return true;
		}

		property.SetValue(target, TargetValueConverter.ConvertValue(value, property.PropertyType), null);
		result = ActionResult.Ok();
		return true;
	}
}
