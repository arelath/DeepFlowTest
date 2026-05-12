namespace DeepFlowTest.Contracts;

using System;

public sealed class ProtocolException : Exception
{
	public ProtocolException(string errorCode, string message)
		: base(message)
	{
		ErrorCode = errorCode;
	}

	public ProtocolException(string errorCode, string message, Exception innerException)
		: base(message, innerException)
	{
		ErrorCode = errorCode;
	}

	public string ErrorCode { get; }
}
