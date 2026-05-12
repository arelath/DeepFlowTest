namespace DeepFlowTest.Interop;

using System;
using System.IO;

internal sealed class StreamWriteLock
{
	private readonly object sync = new();

	public bool TryWrite(Stream stream, object message)
	{
		_ = stream ?? throw new ArgumentNullException(nameof(stream));
		_ = message ?? throw new ArgumentNullException(nameof(message));

		lock (sync)
		{
			MessagePacker.WriteFrame(stream, message);
			return true;
		}
	}
}
