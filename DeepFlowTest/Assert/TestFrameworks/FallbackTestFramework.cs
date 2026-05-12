namespace DeepFlowTest.Assert.TestFrameworks;

internal sealed class FallbackTestFramework : ITestFramework
{
	public bool IsAvailable => true;

	public void Throw(string message)
	{
		throw new AppDriverAssertionException(message);
	}
}
