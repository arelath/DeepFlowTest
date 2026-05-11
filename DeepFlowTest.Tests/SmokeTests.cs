namespace DeepFlowTest.Tests;

using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class SmokeTests
{
	[Test]
	public void ProductConstantsUseDeepFlowTestNames()
	{
		Assert.That(ProtocolConstants.ProductName, Is.EqualTo("DeepFlowTest"));
		Assert.That(ProtocolConstants.PipePrefix, Is.EqualTo("deepflowtest"));
		Assert.That(ProtocolConstants.ProtocolVersion, Is.EqualTo("1"));
	}
}
