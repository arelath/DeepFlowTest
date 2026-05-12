namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
[Category("Integration")]
[Explicit("Requires a published CLI plus a running desktop harness target.")]
public sealed class RealCliReadTests
{
	[Test]
	public void HarnessReadWorkflows()
	{
		Assert.Inconclusive("Run in the integration lane with HelloWorld/BasicTestHarness to cover tree, find, node, screenshot, wait, and pipe-idle workflows.");
	}
}
