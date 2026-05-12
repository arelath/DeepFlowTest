namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
[Category("Integration")]
[Explicit("Requires a published CLI plus a running desktop harness target.")]
public sealed class RealCliActionTests
{
	[Test]
	public void HarnessActionWorkflows()
	{
		Assert.Inconclusive("Run in the integration lane with BasicTestHarness to cover click, focus, type, key, set, raise, invoke, strict mode, and target survival.");
	}
}
