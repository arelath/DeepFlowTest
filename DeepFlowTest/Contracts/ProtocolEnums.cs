namespace DeepFlowTest.Contracts;

using System;
using Newtonsoft.Json;

public enum TreeShape
{
	Flat,
	Nested,
}

public enum MouseButtonKind
{
	Left,
	Right,
	Middle,
}

public enum BindingFailureSeverity
{
	Verbose,
	Information,
	Warning,
	Error,
}

public static class ProtocolValueMapper
{
	public static string FormatImageFormat(ImageFormat format) =>
		format switch
		{
			ImageFormat.Bmp => "bmp",
			ImageFormat.Gif => "gif",
			ImageFormat.Jpeg => "jpeg",
			ImageFormat.Png => "png",
			_ => throw new FormatException($"Unsupported image format '{format}'."),
		};

	public static ImageFormat ParseImageFormat(string? format)
	{
		if (TryParseImageFormat(format, out var imageFormat))
			return imageFormat;

		throw new FormatException($"Unsupported image format '{format}'.");
	}

	public static bool TryParseImageFormat(string? format, out ImageFormat imageFormat)
	{
		switch ((format ?? "png").Trim().ToLowerInvariant())
		{
			case "png":
				imageFormat = ImageFormat.Png;
				return true;
			case "bmp":
				imageFormat = ImageFormat.Bmp;
				return true;
			case "gif":
				imageFormat = ImageFormat.Gif;
				return true;
			case "jpg":
			case "jpeg":
				imageFormat = ImageFormat.Jpeg;
				return true;
			default:
				imageFormat = default;
				return false;
		}
	}

	public static string FormatMouseButton(MouseButtonKind button) =>
		button switch
		{
			MouseButtonKind.Left => "left",
			MouseButtonKind.Right => "right",
			MouseButtonKind.Middle => "middle",
			_ => throw new FormatException($"Unsupported mouse button '{button}'."),
		};

	public static MouseButtonKind ParseMouseButton(string? button)
	{
		if (TryParseMouseButton(button, out var mouseButton))
			return mouseButton;

		throw new FormatException($"Unsupported mouse button '{button}'.");
	}

	public static bool TryParseMouseButton(string? button, out MouseButtonKind mouseButton)
	{
		switch ((button ?? "left").Trim().ToLowerInvariant())
		{
			case "left":
				mouseButton = MouseButtonKind.Left;
				return true;
			case "right":
				mouseButton = MouseButtonKind.Right;
				return true;
			case "middle":
				mouseButton = MouseButtonKind.Middle;
				return true;
			default:
				mouseButton = default;
				return false;
		}
	}

	public static string FormatTreeShape(TreeShape shape) =>
		shape switch
		{
			TreeShape.Flat => "flat",
			TreeShape.Nested => "nested",
			_ => throw new FormatException($"Unsupported tree shape '{shape}'."),
		};

	public static TreeShape ParseTreeShape(string? shape)
	{
		if (TryParseTreeShape(shape, out var treeShape))
			return treeShape;

		throw new FormatException($"Unsupported tree shape '{shape}'.");
	}

	public static bool TryParseTreeShape(string? shape, out TreeShape treeShape)
	{
		switch ((shape ?? "flat").Trim().ToLowerInvariant())
		{
			case "flat":
				treeShape = TreeShape.Flat;
				return true;
			case "nested":
				treeShape = TreeShape.Nested;
				return true;
			default:
				treeShape = default;
				return false;
		}
	}

	public static object ToProtocolValue(object value) =>
		value switch
		{
			ImageFormat imageFormat => FormatImageFormat(imageFormat),
			MouseButtonKind mouseButton => FormatMouseButton(mouseButton),
			TreeShape treeShape => FormatTreeShape(treeShape),
			BindingFailureSeverity severity => FormatBindingFailureSeverity(severity),
			_ => value,
		};

	public static string FormatBindingFailureSeverity(BindingFailureSeverity severity) =>
		severity switch
		{
			BindingFailureSeverity.Verbose => "verbose",
			BindingFailureSeverity.Information => "information",
			BindingFailureSeverity.Warning => "warning",
			BindingFailureSeverity.Error => "error",
			_ => throw new FormatException($"Unsupported binding failure severity '{severity}'."),
		};

	public static BindingFailureSeverity ParseBindingFailureSeverity(string? severity)
	{
		if (TryParseBindingFailureSeverity(severity, out var bindingFailureSeverity))
			return bindingFailureSeverity;

		throw new FormatException($"Unsupported binding failure severity '{severity}'.");
	}

	public static bool TryParseBindingFailureSeverity(string? severity, out BindingFailureSeverity bindingFailureSeverity)
	{
		switch ((severity ?? "warning").Trim().ToLowerInvariant())
		{
			case "verbose":
				bindingFailureSeverity = BindingFailureSeverity.Verbose;
				return true;
			case "info":
			case "information":
				bindingFailureSeverity = BindingFailureSeverity.Information;
				return true;
			case "warn":
			case "warning":
				bindingFailureSeverity = BindingFailureSeverity.Warning;
				return true;
			case "err":
			case "error":
				bindingFailureSeverity = BindingFailureSeverity.Error;
				return true;
			default:
				bindingFailureSeverity = default;
				return false;
		}
	}
}

public sealed class ProtocolImageFormatJsonConverter : JsonConverter
{
	public override bool CanConvert(Type objectType) =>
		(Nullable.GetUnderlyingType(objectType) ?? objectType) == typeof(ImageFormat);

	public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null && Nullable.GetUnderlyingType(objectType) is not null)
			return null;
		if (reader.TokenType == JsonToken.String)
			return ProtocolValueMapper.ParseImageFormat((string?)reader.Value);

		throw new JsonSerializationException("Image format must be a protocol string.");
	}

	public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
	{
		if (value is null)
		{
			writer.WriteNull();
			return;
		}

		writer.WriteValue(ProtocolValueMapper.FormatImageFormat((ImageFormat)value));
	}
}

public sealed class ProtocolMouseButtonJsonConverter : JsonConverter
{
	public override bool CanConvert(Type objectType) =>
		(Nullable.GetUnderlyingType(objectType) ?? objectType) == typeof(MouseButtonKind);

	public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null && Nullable.GetUnderlyingType(objectType) is not null)
			return null;
		if (reader.TokenType == JsonToken.String)
			return ProtocolValueMapper.ParseMouseButton((string?)reader.Value);

		throw new JsonSerializationException("Mouse button must be a protocol string.");
	}

	public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
	{
		if (value is null)
		{
			writer.WriteNull();
			return;
		}

		writer.WriteValue(ProtocolValueMapper.FormatMouseButton((MouseButtonKind)value));
	}
}

public sealed class ProtocolBindingFailureSeverityJsonConverter : JsonConverter
{
	public override bool CanConvert(Type objectType) =>
		(Nullable.GetUnderlyingType(objectType) ?? objectType) == typeof(BindingFailureSeverity);

	public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null && Nullable.GetUnderlyingType(objectType) is not null)
			return null;
		if (reader.TokenType == JsonToken.String)
			return ProtocolValueMapper.ParseBindingFailureSeverity((string?)reader.Value);

		throw new JsonSerializationException("Binding failure severity must be a protocol string.");
	}

	public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
	{
		if (value is null)
		{
			writer.WriteNull();
			return;
		}

		writer.WriteValue(ProtocolValueMapper.FormatBindingFailureSeverity((BindingFailureSeverity)value));
	}
}
