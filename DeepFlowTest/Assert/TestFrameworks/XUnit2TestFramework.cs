namespace DeepFlowTest.Assert.TestFrameworks;

using System;
using System.Reflection;

internal sealed class XUnit2TestFramework : ITestFramework
{
	private Assembly? assembly;

	public bool IsAvailable
	{
		get
		{
			try
			{
				assembly = Assembly.Load(new AssemblyName("xunit.assert"));
				return assembly is not null;
			}
			catch
			{
				return false;
			}
		}
	}

	public void Throw(string message)
	{
		var exceptionType = assembly?.GetType("Xunit.Sdk.XunitException")
			?? throw new NotSupportedException("Failed to create the XUnit assertion type");

		throw (Exception)Activator.CreateInstance(exceptionType, message)!;
	}
}
