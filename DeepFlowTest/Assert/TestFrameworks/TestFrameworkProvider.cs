namespace DeepFlowTest.Assert.TestFrameworks;

internal static class TestFrameworkProvider
{
	public static void Throw(string message) => FallbackTestFramework.Throw(message);
}
