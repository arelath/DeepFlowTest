namespace DeepFlowTest.Assert.TestFrameworks;

using System;
using System.Reflection;

internal abstract class LateBoundTestFramework : ITestFramework
{
	private Assembly? assembly;

	public bool IsAvailable
	{
		get
		{
			var prefix = AssemblyName + ",";
			assembly = Array.Find(
				AppDomain.CurrentDomain.GetAssemblies(),
				candidate => candidate.FullName?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true);
			return assembly is not null;
		}
	}

	public void Throw(string message)
	{
		var exceptionType = assembly?.GetType(ExceptionFullName);
		if (exceptionType is null)
			throw new NotSupportedException($"Failed to create the assertion exception for the current test framework: \"{ExceptionFullName}, {assembly?.FullName}\"");

		throw (Exception)Activator.CreateInstance(exceptionType, message)!;
	}

	protected internal abstract string AssemblyName { get; }

	protected abstract string ExceptionFullName { get; }
}
