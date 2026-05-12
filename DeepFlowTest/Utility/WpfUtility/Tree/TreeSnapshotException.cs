namespace DeepFlowTest.Utility.WpfUtility.Tree;

using System;

public sealed class TreeSnapshotException : Exception
{
	public TreeSnapshotException(string message, string errorCode)
		: base(message)
	{
		ErrorCode = errorCode;
	}

	public string ErrorCode { get; }
}
