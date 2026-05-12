namespace DeepFlowTest.Assert.TestFrameworks;

internal sealed class MSTestFrameworkV2 : LateBoundTestFramework
{
	protected internal override string AssemblyName => "Microsoft.VisualStudio.TestPlatform.TestFramework";

	protected override string ExceptionFullName => "Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException";
}
