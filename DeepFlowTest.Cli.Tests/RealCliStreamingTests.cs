namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
[Category("Integration")]
[Explicit("Requires a published CLI plus a running desktop harness target.")]
public sealed class RealCliStreamingTests
{
	[Test]
	public void HarnessStreamingWorkflows()
	{
		Assert.Inconclusive("Run in the integration lane with BasicTestHarness to read real visual-tree, delta, screenshot, and event-log frames.");
	}
}
