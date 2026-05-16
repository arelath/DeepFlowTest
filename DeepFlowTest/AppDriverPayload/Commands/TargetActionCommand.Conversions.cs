namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

internal static class TargetValueConverter
{
	private static readonly IReadOnlyDictionary<string, FontWeight> FontWeightsByName =
		new Dictionary<string, FontWeight>(StringComparer.OrdinalIgnoreCase)
		{
			[nameof(FontWeights.Black)] = FontWeights.Black,
			[nameof(FontWeights.Bold)] = FontWeights.Bold,
			[nameof(FontWeights.DemiBold)] = FontWeights.DemiBold,
			[nameof(FontWeights.ExtraBlack)] = FontWeights.ExtraBlack,
			[nameof(FontWeights.ExtraBold)] = FontWeights.ExtraBold,
			[nameof(FontWeights.ExtraLight)] = FontWeights.ExtraLight,
			[nameof(FontWeights.Heavy)] = FontWeights.Heavy,
			[nameof(FontWeights.Light)] = FontWeights.Light,
			[nameof(FontWeights.Medium)] = FontWeights.Medium,
			[nameof(FontWeights.Normal)] = FontWeights.Normal,
			[nameof(FontWeights.Regular)] = FontWeights.Regular,
			[nameof(FontWeights.SemiBold)] = FontWeights.SemiBold,
			[nameof(FontWeights.Thin)] = FontWeights.Thin,
			[nameof(FontWeights.UltraBlack)] = FontWeights.UltraBlack,
			[nameof(FontWeights.UltraBold)] = FontWeights.UltraBold,
			[nameof(FontWeights.UltraLight)] = FontWeights.UltraLight,
		};

	private static readonly IReadOnlyDictionary<string, FontStyle> FontStylesByName =
		new Dictionary<string, FontStyle>(StringComparer.OrdinalIgnoreCase)
		{
			[nameof(FontStyles.Italic)] = FontStyles.Italic,
			[nameof(FontStyles.Normal)] = FontStyles.Normal,
			[nameof(FontStyles.Oblique)] = FontStyles.Oblique,
		};

	private static readonly IReadOnlyDictionary<string, FontStretch> FontStretchesByName =
		new Dictionary<string, FontStretch>(StringComparer.OrdinalIgnoreCase)
		{
			[nameof(FontStretches.Condensed)] = FontStretches.Condensed,
			[nameof(FontStretches.Expanded)] = FontStretches.Expanded,
			[nameof(FontStretches.ExtraCondensed)] = FontStretches.ExtraCondensed,
			[nameof(FontStretches.ExtraExpanded)] = FontStretches.ExtraExpanded,
			[nameof(FontStretches.Medium)] = FontStretches.Medium,
			[nameof(FontStretches.Normal)] = FontStretches.Normal,
			[nameof(FontStretches.SemiCondensed)] = FontStretches.SemiCondensed,
			[nameof(FontStretches.SemiExpanded)] = FontStretches.SemiExpanded,
			[nameof(FontStretches.UltraCondensed)] = FontStretches.UltraCondensed,
			[nameof(FontStretches.UltraExpanded)] = FontStretches.UltraExpanded,
		};

	public static object? ConvertValue(object? value, Type targetType)
	{
		if (value is null)
			return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
				? Activator.CreateInstance(targetType)
				: null;

		var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
		if (underlyingType.IsInstanceOfType(value))
			return value;

		if (underlyingType.IsEnum)
			return Enum.Parse(underlyingType, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);

		if (value is string text)
		{
			var converted = ConvertFromInvariantString(text, underlyingType);
			if (converted is not null)
				return converted;
		}

		var sourceConverter = TypeDescriptor.GetConverter(value);
		if (sourceConverter.CanConvertTo(underlyingType))
			return sourceConverter.ConvertTo(null, CultureInfo.InvariantCulture, value, underlyingType);

		var targetConverter = TypeDescriptor.GetConverter(underlyingType);
		if (targetConverter.CanConvertFrom(value.GetType()))
			return targetConverter.ConvertFrom(null, CultureInfo.InvariantCulture, value);

		return Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
	}

	private static object? ConvertFromInvariantString(string text, Type targetType)
	{
		if (targetType == typeof(SolidColorBrush))
		{
			var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
			if (brush.CanFreeze)
				brush.Freeze();
			return brush;
		}

		if (targetType == typeof(FontFamily))
			return new FontFamily(text);

		if (targetType == typeof(Size))
			return Size.Parse(text);

		if (targetType == typeof(Point))
			return Point.Parse(text);

		if (targetType == typeof(Thickness))
			return (Thickness)new ThicknessConverter().ConvertFromString(null, CultureInfo.InvariantCulture, text)!;

		if (targetType == typeof(Rect))
			return Rect.Parse(text);

		if (targetType == typeof(FontWeight))
			return ConvertFontWeight(text);

		if (targetType == typeof(FontStyle))
			return ConvertFontStyle(text);

		if (targetType == typeof(FontStretch))
			return ConvertFontStretch(text);

		var converter = TypeDescriptor.GetConverter(targetType);
		return converter.CanConvertFrom(typeof(string))
			? converter.ConvertFromInvariantString(text)
			: null;
	}

	private static FontWeight ConvertFontWeight(string text)
	{
		var value = text.Trim();
		if (FontWeightsByName.TryGetValue(value, out var weight))
			return weight;

		if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericWeight))
			return FontWeight.FromOpenTypeWeight(numericWeight);

		throw new ArgumentOutOfRangeException(nameof(text), $"Could not convert '{text}' to a FontWeight.");
	}

	private static FontStyle ConvertFontStyle(string text)
	{
		var value = text.Trim();
		if (FontStylesByName.TryGetValue(value, out var style))
			return style;

		throw new ArgumentOutOfRangeException(nameof(text), $"Could not convert '{text}' to a FontStyle.");
	}

	private static FontStretch ConvertFontStretch(string text)
	{
		var value = text.Trim();
		if (FontStretchesByName.TryGetValue(value, out var stretch))
			return stretch;

		if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericStretch))
			return FontStretch.FromOpenTypeStretch(numericStretch);

		throw new ArgumentOutOfRangeException(nameof(text), $"Could not convert '{text}' to a FontStretch.");
	}

	public static object? UnwrapJsonValue(object? value)
	{
		return value switch
		{
			JValue jValue => jValue.Value,
			JToken token => token.ToObject<object>(),
			_ => value,
		};
	}
}
