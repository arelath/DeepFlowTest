namespace DeepFlowTest.Assert.TestFrameworks;

using System;
using System.Reflection;

internal sealed class NSpecFramework : ITestFramework
{
	private Assembly? assembly;

	public bool IsAvailable
	{
		get
		{
			assembly = Array.Find(
				AppDomain.CurrentDomain.GetAssemblies(),
				candidate => candidate.FullName?.StartsWith("nspec,", StringComparison.OrdinalIgnoreCase) == true);
			return assembly?.GetName().Version?.Major >= 2;
		}
	}

	public void Throw(string message)
	{
		var exceptionType = assembly?.GetType("NSpec.Domain.AssertionException")
			?? throw new NotSupportedException("Failed to create the NSpec assertion type");

		throw (Exception)Activator.CreateInstance(exceptionType, message)!;
	}
}
