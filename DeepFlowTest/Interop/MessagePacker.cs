namespace DeepFlowTest.Interop;

using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class MessagePacker
{
	private const int LengthPrefixByteCount = sizeof(int);
	public const int MaxFrameLength = 512 * 1024 * 1024;
	private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
	private static int maxFrameLengthLimit = MaxFrameLength;

	public static byte[] Pack(object message)
	{
		if (message is null)
			throw new ProtocolException(ProtocolConstants.ErrorCodes.ProtocolError, "Message frames cannot contain null payloads.");

		var payload = Compress(message);
		ValidateFrameLength(payload.Length);
		return payload;
	}

	public static object Unpack(byte[] rawMessage)
	{
		if (rawMessage is null || rawMessage.Length == 0)
			throw new ProtocolException(ProtocolConstants.ErrorCodes.MalformedFrame, "Message frame payload is empty.");

		ValidateFrameLength(rawMessage.Length);

		try
		{
			return DeserializeCompressed(new MemoryStream(rawMessage, writable: false));
		}
		catch (JsonException ex)
		{
			throw new ProtocolException(ProtocolConstants.ErrorCodes.ProtocolError, "Message frame JSON is invalid.", ex);
		}
		catch (InvalidDataException ex)
		{
			throw new ProtocolException(ProtocolConstants.ErrorCodes.MalformedFrame, "Message frame compression payload is invalid.", ex);
		}
	}

	public static T ConvertTo<T>(object message)
	{
		return (T)ConvertTo(message, typeof(T));
	}

	public static object ConvertTo(object message, Type targetType)
	{
		_ = targetType ?? throw new ArgumentNullException(nameof(targetType));
		if (message is null)
			throw new ProtocolException(ProtocolConstants.ErrorCodes.ProtocolError, "Message cannot be converted because it is null.");

		if (message is not null && targetType.IsInstanceOfType(message))
			return message;

		var serializer = JsonSerializer.Create(SerializerSettings);
		var token = message is JToken jToken ? jToken.DeepClone() : JToken.FromObject(message!, serializer);
		if (token is JObject jObject)
			jObject.Remove("$type");

		return token.ToObject(targetType, serializer)
			?? throw new ProtocolException(ProtocolConstants.ErrorCodes.ProtocolError, $"Message could not be converted to {targetType.FullName}.");
	}

	public static void WriteFrame(Stream stream, object message)
	{
		_ = stream ?? throw new ArgumentNullException(nameof(stream));

		var payload = Pack(message);
		var lengthBytes = BitConverter.GetBytes(payload.Length);
		stream.Write(lengthBytes, 0, lengthBytes.Length);
		stream.Write(payload, 0, payload.Length);
		stream.Flush();
	}

	public static async Task WriteFrameAsync(Stream stream, object message, CancellationToken cancellationToken = default)
	{
		_ = stream ?? throw new ArgumentNullException(nameof(stream));

		var payload = Pack(message);
		var lengthBytes = BitConverter.GetBytes(payload.Length);
		await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken).ConfigureAwait(false);
		await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
		await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	public static MessageFrame ReadFrame(Stream stream)
	{
		_ = stream ?? throw new ArgumentNullException(nameof(stream));

		var payload = ReadFramePayload(stream);
		if (payload is null)
			return MessageFrame.None;

		var buffer = payload.Value.Buffer;
		try
		{
			return MessageFrame.FromMessage(UnpackBuffer(buffer, payload.Value.Length));
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	public static async Task<MessageFrame> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
	{
		_ = stream ?? throw new ArgumentNullException(nameof(stream));

		var payload = await ReadFramePayloadAsync(stream, cancellationToken).ConfigureAwait(false);
		if (payload is null)
			return MessageFrame.None;

		var buffer = payload.Value.Buffer;
		try
		{
			return MessageFrame.FromMessage(UnpackBuffer(buffer, payload.Value.Length));
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private static object UnpackBuffer(byte[] buffer, int length)
	{
		try
		{
			return DeserializeCompressed(new MemoryStream(buffer, 0, length, writable: false));
		}
		catch (JsonException ex)
		{
			throw new ProtocolException(ProtocolConstants.ErrorCodes.ProtocolError, "Message frame JSON is invalid.", ex);
		}
		catch (InvalidDataException ex)
		{
			throw new ProtocolException(ProtocolConstants.ErrorCodes.MalformedFrame, "Message frame compression payload is invalid.", ex);
		}
	}

	private static byte[] Compress(object message)
	{
		using var compressedStream = new MemoryStream();
		using (var compressorStream = new DeflateStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
		using (var streamWriter = new StreamWriter(compressorStream, Utf8NoBom))
		using (var jsonWriter = new JsonTextWriter(streamWriter))
		{
			JsonSerializer.Create(SerializerSettings).Serialize(jsonWriter, message);
		}

		return compressedStream.ToArray();
	}

	private static object DeserializeCompressed(Stream compressedStream)
	{
		using var decompressorStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
		using var streamReader = new StreamReader(decompressorStream, Utf8NoBom);
		using var jsonReader = new JsonTextReader(streamReader);
		jsonReader.MaxDepth = SerializerSettings.MaxDepth;
		return JsonSerializer.Create(SerializerSettings).Deserialize(jsonReader)
			?? throw new ProtocolException(ProtocolConstants.ErrorCodes.ProtocolError, "Message frame payload deserialized to null.");
	}

	private static RentedPayload? ReadFramePayload(Stream stream)
	{
		var lengthBytes = ReadExact(stream, LengthPrefixByteCount);
		if (lengthBytes is null)
			return null;

		var length = BitConverter.ToInt32(lengthBytes, 0);
		ValidateFrameLength(length);
		return ReadExactRented(stream, length);
	}

	private static async Task<RentedPayload?> ReadFramePayloadAsync(Stream stream, CancellationToken cancellationToken)
	{
		var lengthBytes = await ReadExactAsync(stream, LengthPrefixByteCount, cancellationToken).ConfigureAwait(false);
		if (lengthBytes is null)
			return null;

		var length = BitConverter.ToInt32(lengthBytes, 0);
		ValidateFrameLength(length);
		return await ReadExactRentedAsync(stream, length, cancellationToken).ConfigureAwait(false);
	}

	private static byte[]? ReadExact(Stream stream, int count)
	{
		var buffer = new byte[count];
		var offset = 0;
		while (offset < count)
		{
			var read = stream.Read(buffer, offset, count - offset);
			if (read == 0)
			{
				if (offset == 0)
					return null;

				throw new ProtocolException(ProtocolConstants.ErrorCodes.MalformedFrame, "The stream ended in the middle of a message frame.");
			}

			offset += read;
		}

		return buffer;
	}

	private static async Task<byte[]?> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
	{
		var buffer = new byte[count];
		var offset = 0;
		while (offset < count)
		{
			var read = await stream.ReadAsync(buffer, offset, count - offset, cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				if (offset == 0)
					return null;

				throw new ProtocolException(ProtocolConstants.ErrorCodes.MalformedFrame, "The stream ended in the middle of a message frame.");
			}

			offset += read;
		}

		return buffer;
	}

	private static RentedPayload ReadExactRented(Stream stream, int count)
	{
		var buffer = ArrayPool<byte>.Shared.Rent(count);
		try
		{
			var offset = 0;
			while (offset < count)
			{
				var read = stream.Read(buffer, offset, count - offset);
				if (read == 0)
					throw new ProtocolException(ProtocolConstants.ErrorCodes.MalformedFrame, "The stream ended before the full message frame was read.");

				offset += read;
			}

			return new RentedPayload(buffer, count);
		}
		catch
		{
			ArrayPool<byte>.Shared.Return(buffer);
			throw;
		}
	}

	private static async Task<RentedPayload> ReadExactRentedAsync(Stream stream, int count, CancellationToken cancellationToken)
	{
		var buffer = ArrayPool<byte>.Shared.Rent(count);
		try
		{
			var offset = 0;
			while (offset < count)
			{
				var read = await stream.ReadAsync(buffer, offset, count - offset, cancellationToken).ConfigureAwait(false);
				if (read == 0)
					throw new ProtocolException(ProtocolConstants.ErrorCodes.MalformedFrame, "The stream ended before the full message frame was read.");

				offset += read;
			}

			return new RentedPayload(buffer, count);
		}
		catch
		{
			ArrayPool<byte>.Shared.Return(buffer);
			throw;
		}
	}

	private static void ValidateFrameLength(int length)
	{
		if (length <= 0)
			throw new ProtocolException(ProtocolConstants.ErrorCodes.MalformedFrame, $"Invalid message frame length: {length}.");

		if (length > maxFrameLengthLimit)
			throw new ProtocolException(ProtocolConstants.ErrorCodes.MalformedFrame, $"Message frame length {length} exceeds the limit of {maxFrameLengthLimit} bytes.");
	}

	private static IDisposable OverrideMaxFrameLengthForTests(int maxFrameLength)
	{
		var previous = maxFrameLengthLimit;
		maxFrameLengthLimit = maxFrameLength;
		return new RestoreMaxFrameLength(previous);
	}

	public readonly struct MessageFrame
	{
		private MessageFrame(bool hasFrame, object? message)
		{
			HasFrame = hasFrame;
			Message = message;
		}

		public bool HasFrame { get; }

		public object? Message { get; }

		public static MessageFrame None => new(false, null);

		public static MessageFrame FromMessage(object message)
		{
			if (message is null)
				throw new ProtocolException(ProtocolConstants.ErrorCodes.ProtocolError, "Message frames cannot contain null payloads.");

			return new MessageFrame(true, message);
		}
	}

	private readonly struct RentedPayload
	{
		public RentedPayload(byte[] buffer, int length)
		{
			Buffer = buffer;
			Length = length;
		}

		public byte[] Buffer { get; }

		public int Length { get; }
	}

	private sealed class RestoreMaxFrameLength : IDisposable
	{
		private readonly int previous;

		public RestoreMaxFrameLength(int previous)
		{
			this.previous = previous;
		}

		public void Dispose()
		{
			maxFrameLengthLimit = previous;
		}
	}

	private static JsonSerializerSettings SerializerSettings { get; } = new()
	{
		ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
		TypeNameHandling = TypeNameHandling.All,
		SerializationBinder = CrossRuntimeSerializationBinder.Instance,
		MaxDepth = 1000,
	};
}
