namespace DeepFlowTest.Cli;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepFlowTest.Contracts;

internal sealed class CliImageFormatJsonConverter : JsonConverter<ImageFormat>
{
	public override ImageFormat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			try
			{
				return ImageFormatExtensions.ParseProtocolString(reader.GetString());
			}
			catch (FormatException ex)
			{
				throw new JsonException(ex.Message, ex);
			}
		}

		throw new JsonException("Image format must be a string.");
	}

	public override void Write(Utf8JsonWriter writer, ImageFormat value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToProtocolString());
	}
}

internal sealed class CliTreeShapeJsonConverter : JsonConverter<TreeShape>
{
	public override TreeShape Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			try
			{
				return ProtocolValueMapper.ParseTreeShape(reader.GetString());
			}
			catch (FormatException ex)
			{
				throw new JsonException(ex.Message, ex);
			}
		}

		throw new JsonException("Tree shape must be a string.");
	}

	public override void Write(Utf8JsonWriter writer, TreeShape value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(ProtocolValueMapper.FormatTreeShape(value));
	}
}

internal sealed class CliMouseButtonJsonConverter : JsonConverter<MouseButtonKind>
{
	public override MouseButtonKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			try
			{
				return ProtocolValueMapper.ParseMouseButton(reader.GetString());
			}
			catch (FormatException ex)
			{
				throw new JsonException(ex.Message, ex);
			}
		}

		throw new JsonException("Mouse button must be a string.");
	}

	public override void Write(Utf8JsonWriter writer, MouseButtonKind value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(ProtocolValueMapper.FormatMouseButton(value));
	}
}
