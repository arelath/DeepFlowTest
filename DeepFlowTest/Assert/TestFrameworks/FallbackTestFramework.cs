namespace DeepFlowTest.Assert.TestFrameworks;

internal static class FallbackTestFramework
{
	public static void Throw(string message)
	{
		throw new AppDriverAssertionException(message);
	}
}
