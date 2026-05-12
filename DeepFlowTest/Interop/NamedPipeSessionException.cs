namespace DeepFlowTest.Interop;

using System;

public sealed class NamedPipeSessionException : Exception
{
	public NamedPipeSessionException(string errorCode, string message)
		: base(message)
	{
		ErrorCode = errorCode;
	}

	public NamedPipeSessionException(string errorCode, string message, Exception innerException)
		: base(message, innerException)
	{
		ErrorCode = errorCode;
	}

	public string ErrorCode { get; }
}
