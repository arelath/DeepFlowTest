namespace DeepFlowTest.Tests;

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class MessagePackerTests
{
	[Test]
	public void RoundTripsSmallCommand()
	{
		var command = new HelloCommandRequest();

		var unpacked = MessagePacker.ConvertTo<HelloCommandRequest>(MessagePacker.Unpack(MessagePacker.Pack(command)));

		Assert.That(unpacked.Kind, Is.EqualTo(ProtocolConstants.Commands.Hello));
		Assert.That(unpacked.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));
	}

	[Test]
	public void RoundTripsLargeResponse()
	{
		var response = new StandardIpcResponse { Success = true, Status = new string('x', 1024 * 1024) };

		var unpacked = MessagePacker.ConvertTo<StandardIpcResponse>(MessagePacker.Unpack(MessagePacker.Pack(response)));

		Assert.That(unpacked.Status, Has.Length.EqualTo(1024 * 1024));
	}

	[Test]
	public void RejectsNullPayload()
	{
		Assert.That(() => MessagePacker.Pack(null!), Throws.TypeOf<ProtocolException>());
	}

	[Test]
	public void RejectsOversizedWrites()
	{
		using var _ = OverrideMaxFrameLength(8);

		Assert.That(
			() => MessagePacker.WriteFrame(new MemoryStream(), new StandardIpcResponse { Status = new string('x', 1024) }),
			Throws.TypeOf<ProtocolException>());
	}

	[Test]
	public void RejectsOversizedAsyncWrites()
	{
		using var _ = OverrideMaxFrameLength(8);

		Assert.That(
			async () => await MessagePacker.WriteFrameAsync(new MemoryStream(), new StandardIpcResponse { Status = new string('x', 1024) }),
			Throws.TypeOf<ProtocolException>());
	}

	[Test]
	public void RejectsMalformedAndOversizedLengths()
	{
		Assert.That(() => MessagePacker.ReadFrame(new MemoryStream(BitConverter.GetBytes(-1))), Throws.TypeOf<ProtocolException>());
		Assert.That(() => MessagePacker.ReadFrame(new MemoryStream(BitConverter.GetBytes(MessagePacker.MaxFrameLength + 1))), Throws.TypeOf<ProtocolException>());
	}

	[Test]
	public void RejectsTruncatedStream()
	{
		using var stream = new MemoryStream();
		stream.Write(BitConverter.GetBytes(8), 0, 4);
		stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
		stream.Position = 0;

		Assert.That(() => MessagePacker.ReadFrame(stream), Throws.TypeOf<ProtocolException>());
	}

	[Test]
	public void RejectsUnknownSerializedType()
	{
		using var stream = CreateFrame(@"{""$type"":""No.Such.Type, No.Such.Assembly"",""Kind"":""HelloCommand""}");

		Assert.That(() => MessagePacker.ReadFrame(stream), Throws.TypeOf<ProtocolException>());
	}

	[Test]
	public void RejectsAsyncTruncatedStream()
	{
		using var stream = new MemoryStream();
		stream.Write(BitConverter.GetBytes(8), 0, 4);
		stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
		stream.Position = 0;

		Assert.That(
			async () => await MessagePacker.ReadFrameAsync(stream),
			Throws.TypeOf<ProtocolException>());
	}

	[Test]
	public void AsyncCancellationDuringPayloadReadPropagates()
	{
		using var stream = new CancelAfterLengthStream();

		Assert.That(
			async () => await MessagePacker.ReadFrameAsync(stream),
			Throws.TypeOf<OperationCanceledException>());
	}

	private static IDisposable OverrideMaxFrameLength(int maxFrameLength) =>
		MessagePacker.OverrideMaxFrameLengthForTests(maxFrameLength);

	private static MemoryStream CreateFrame(string json)
	{
		using var payload = new MemoryStream();
		using (var deflate = new DeflateStream(payload, CompressionLevel.Optimal, leaveOpen: true))
		{
			var bytes = Encoding.UTF8.GetBytes(json);
			deflate.Write(bytes, 0, bytes.Length);
		}

		var stream = new MemoryStream();
		var compressed = payload.ToArray();
		stream.Write(BitConverter.GetBytes(compressed.Length), 0, 4);
		stream.Write(compressed, 0, compressed.Length);
		stream.Position = 0;
		return stream;
	}

	private sealed class CancelAfterLengthStream : Stream
	{
		private int readCount;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			if (readCount++ == 0)
			{
				BitConverter.GetBytes(8).CopyTo(buffer, offset);
				return Task.FromResult(4);
			}

			throw new OperationCanceledException();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}
	}
}
