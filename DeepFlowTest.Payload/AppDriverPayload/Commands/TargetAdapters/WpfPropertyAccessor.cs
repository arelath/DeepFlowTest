namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Reflection;
using System.Windows;
using DeepFlowTest.AppDriverPayload.Commands;

internal static class WpfPropertyAccessor
{
	public static ActionResult SetProperty(object target, string propertyName, object? value)
	{
		if (TargetPropertyAccessor.TrySetClrProperty(target, propertyName, value, out var result))
			return result;

		if (target is DependencyObject dependencyObject && TryFindDependencyProperty(target.GetType(), propertyName, out var dependencyProperty))
		{
			dependencyObject.SetValue(dependencyProperty, TargetValueConverter.ConvertValue(value, dependencyProperty.PropertyType));
			return ActionResult.Ok();
		}

		return ActionResult.Unsupported($"Property '{propertyName}' was not found.");
	}

	private static bool TryFindDependencyProperty(Type targetType, string propertyName, out DependencyProperty dependencyProperty)
	{
		var fieldName = propertyName.EndsWith("Property", StringComparison.Ordinal) ? propertyName : propertyName + "Property";
		for (var type = targetType; type is not null; type = type.BaseType)
		{
			var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
			if (field?.GetValue(null) is DependencyProperty found)
			{
				dependencyProperty = found;
				return true;
			}
		}

		dependencyProperty = null!;
		return false;
	}
}
