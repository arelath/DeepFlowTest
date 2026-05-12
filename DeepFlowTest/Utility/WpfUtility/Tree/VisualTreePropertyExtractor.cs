namespace DeepFlowTest.Utility.WpfUtility.Tree;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public sealed class VisualTreePropertyExtractor
{
	public static readonly IReadOnlyList<string> DefaultPropertyNames = new[]
	{
		"Name",
		"AutomationProperties.Name",
		"AutomationProperties.AutomationId",
		"Text",
		"Content",
		"IsVisible",
		"IsEnabled",
	};

	public Dictionary<string, object?> Extract(object target, IEnumerable<string>? requestedPropertyNames = null)
	{
		_ = target ?? throw new ArgumentNullException(nameof(target));

		var propertyNames = NormalizeRequestedPropertyNames(requestedPropertyNames);
		var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

		foreach (var propertyName in propertyNames)
		{
			properties[propertyName] = TryReadProperty(target, propertyName, out var value, out var error)
				? NormalizeValue(value)
				: error;
		}

		return properties;
	}

	private static IReadOnlyList<string> NormalizeRequestedPropertyNames(IEnumerable<string>? requestedPropertyNames)
	{
		var names = requestedPropertyNames ?? DefaultPropertyNames;
		return names
			.Where(static name => string.IsNullOrWhiteSpace(name) == false)
			.Select(static name => name.Trim())
			.Distinct(StringComparer.Ordinal)
			.ToArray();
	}

	private static bool TryReadProperty(object target, string propertyName, out object? value, out PropertyExtractionError? error)
	{
		try
		{
			if (TryReadSpecialProperty(target, propertyName, out value))
			{
				error = null;
				return true;
			}

			if (TryReadAttachedProperty(target, propertyName, out value))
			{
				error = null;
				return true;
			}

			if (TryReadClrProperty(target, propertyName, out value))
			{
				error = null;
				return true;
			}

			if (TryReadDependencyProperty(target, propertyName, out value))
			{
				error = null;
				return true;
			}

			error = PropertyExtractionError.Missing(propertyName);
			value = null;
			return false;
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			value = null;
			error = PropertyExtractionError.Failed(propertyName, ex.InnerException);
			return false;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			value = null;
			error = PropertyExtractionError.Failed(propertyName, ex);
			return false;
		}
	}

	private static bool TryReadSpecialProperty(object target, string propertyName, out object? value)
	{
		value = null;
		if (target is IntPtr hwnd && TryReadNativeWindowProperty(hwnd, propertyName, out value))
			return true;

		switch (propertyName)
		{
			case "Xaml":
				if (target is DependencyObject)
				{
					value = XamlWriter.Save(target);
					return true;
				}

				return false;
			case "ResourceKeys":
				if (target is ResourceDictionary resourceDictionary)
				{
					value = resourceDictionary.Keys.Cast<object?>()
						.Select(static key => Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty)
						.ToArray();
					return true;
				}

				return false;
			case "Source":
				if (target is ResourceDictionary sourceDictionary)
				{
					value = sourceDictionary.Source?.ToString();
					return true;
				}

				return false;
			case "MergedDictionaryCount":
				if (target is ResourceDictionary mergedDictionary)
				{
					value = mergedDictionary.MergedDictionaries.Count;
					return true;
				}

				return false;
			case "ResourceOrigin":
				if (target is ResourceDictionary originDictionary)
				{
					value = originDictionary.Source is null ? "local" : "merged";
					return true;
				}

				if (target is SystemResourceRoot)
				{
					value = "system";
					return true;
				}

				return false;
			case "ImageMetadata":
				if (TryReadImageMetadata(target, out value))
					return true;

				return false;
			case "Bindings":
				if (target is DependencyObject dependencyObject)
				{
					value = ReadBindings(dependencyObject);
					return true;
				}

				return false;
			default:
				return false;
		}
	}

	private static bool TryReadNativeWindowProperty(IntPtr hwnd, string propertyName, out object? value)
	{
		value = propertyName switch
		{
			"ClassName" => NativeDialogService.GetClassName(hwnd),
			"Text" or "Name" or "Title" => NativeDialogService.GetWindowText(hwnd),
			"IsVisible" => NativeDialogService.IsVisible(hwnd),
			"IsEnabled" => NativeDialogService.IsEnabled(hwnd),
			"ControlId" => NativeDialogService.GetControlId(hwnd),
			"Hwnd" => hwnd.ToInt64(),
			_ => null,
		};

		return propertyName is "ClassName" or "Text" or "Name" or "Title" or "IsVisible" or "IsEnabled" or "ControlId" or "Hwnd";
	}

	private static bool TryReadImageMetadata(object target, out object? value)
	{
		var source = target switch
		{
			Image image => image.Source,
			ImageSource imageSource => imageSource,
			_ => null,
		};
		if (source is null)
		{
			value = null;
			return false;
		}

		var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["Type"] = source.GetType().Name,
			["Width"] = source.Width,
			["Height"] = source.Height,
		};
		if (source is BitmapSource bitmapSource)
		{
			metadata["PixelWidth"] = bitmapSource.PixelWidth;
			metadata["PixelHeight"] = bitmapSource.PixelHeight;
		}

		value = metadata;
		return true;
	}

	private static IReadOnlyDictionary<string, object?> ReadBindings(DependencyObject target)
	{
		var bindings = new Dictionary<string, object?>(StringComparer.Ordinal);
		var localValues = target.GetLocalValueEnumerator();
		while (localValues.MoveNext())
		{
			var entry = localValues.Current;
			if (BindingOperations.GetBindingExpressionBase(target, entry.Property) is not { } expression)
				continue;

			var summary = new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["Status"] = expression.Status.ToString(),
			};
			if (expression is BindingExpression bindingExpression)
				summary["Path"] = bindingExpression.ParentBinding.Path?.Path;

			bindings[entry.Property.Name] = summary;
		}

		return bindings;
	}

	private static bool TryReadAttachedProperty(object target, string propertyName, out object? value)
	{
		value = null;
		if (target is not DependencyObject dependencyObject)
			return false;

		switch (propertyName)
		{
			case "AutomationProperties.Name":
			case "AutomationName":
				value = AutomationProperties.GetName(dependencyObject);
				return true;
			case "AutomationProperties.AutomationId":
			case "AutomationId":
				value = AutomationProperties.GetAutomationId(dependencyObject);
				return true;
			default:
				return false;
		}
	}

	private static bool TryReadClrProperty(object target, string propertyName, out object? value)
	{
		value = null;
		var property = target.GetType().GetProperty(
			propertyName,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);

		if (property is null || property.CanRead == false || property.GetIndexParameters().Length != 0)
			return false;

		value = property.GetValue(target, null);
		return true;
	}

	private static bool TryReadDependencyProperty(object target, string propertyName, out object? value)
	{
		value = null;
		if (target is not DependencyObject dependencyObject)
			return false;

		var dependencyProperty = FindDependencyProperty(target.GetType(), propertyName);
		if (dependencyProperty is null)
			return false;

		value = dependencyObject.GetValue(dependencyProperty);
		return true;
	}

	private static DependencyProperty? FindDependencyProperty(Type targetType, string propertyName)
	{
		var fieldName = propertyName.EndsWith("Property", StringComparison.Ordinal)
			? propertyName
			: propertyName + "Property";

		for (var type = targetType; type is not null; type = type.BaseType)
		{
			var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
			if (field?.GetValue(null) is DependencyProperty dependencyProperty)
				return dependencyProperty;
		}

		return null;
	}

	private static object? NormalizeValue(object? value)
	{
		if (value is null || value == DependencyProperty.UnsetValue)
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

		if (value is Point point)
			return new Dictionary<string, object?>
			{
				["X"] = point.X,
				["Y"] = point.Y,
			};

		if (value is Size size)
			return new Dictionary<string, object?>
			{
				["Width"] = size.Width,
				["Height"] = size.Height,
			};

		if (value is Rect rect)
			return new Dictionary<string, object?>
			{
				["X"] = rect.X,
				["Y"] = rect.Y,
				["Width"] = rect.Width,
				["Height"] = rect.Height,
			};

		if (value is Thickness thickness)
			return new Dictionary<string, object?>
			{
				["Left"] = thickness.Left,
				["Top"] = thickness.Top,
				["Right"] = thickness.Right,
				["Bottom"] = thickness.Bottom,
			};

		if (value is Array array)
		{
			var normalized = new List<object?>(array.Length);
			foreach (var item in array)
				normalized.Add(NormalizeValue(item));
			return normalized;
		}

		if (value is IDictionary dictionary)
		{
			var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
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
}

public sealed class PropertyExtractionError
{
	[Newtonsoft.Json.JsonConstructor]
	public PropertyExtractionError(string propertyName, string errorCode, string message)
	{
		PropertyName = propertyName;
		ErrorCode = errorCode;
		Message = message;
	}

	public string PropertyName { get; }

	public string ErrorCode { get; }

	public string Message { get; }

	public static PropertyExtractionError Missing(string propertyName) =>
		new(propertyName, "missing-property", $"Property '{propertyName}' was not found.");

	public static PropertyExtractionError Failed(string propertyName, Exception exception) =>
		new(propertyName, "property-read-failed", exception.Message);
}
