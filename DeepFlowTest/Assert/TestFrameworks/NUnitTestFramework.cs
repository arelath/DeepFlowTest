namespace DeepFlowTest.Assert.TestFrameworks;

internal sealed class NUnitTestFramework : LateBoundTestFramework
{
	protected internal override string AssemblyName => "nunit.framework";

	protected override string ExceptionFullName => "NUnit.Framework.AssertionException";
}
