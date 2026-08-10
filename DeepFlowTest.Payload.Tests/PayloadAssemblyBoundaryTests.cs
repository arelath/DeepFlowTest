namespace DeepFlowTest.Tests;

using System.Reflection;
using DeepFlowTest.AppDriverPayload;
using NUnit.Framework;

[TestFixture]
public sealed class PayloadAssemblyBoundaryTests
{
	[Test]
	public void PayloadPreservesWireIdentityWithoutClientImplementation()
	{
		var assembly = typeof(AppDriverPayload).Assembly;

		Assert.That(assembly.GetName().Name, Is.EqualTo("DeepFlowTest"));
		Assert.That(assembly.GetType("DeepFlowTest.AppDriver"), Is.Null);
		Assert.That(assembly.GetType("DeepFlowTest.AppConnection"), Is.Null);
		Assert.That(assembly.GetType("DeepFlowTest.DefaultAppDriverBackend"), Is.Null);
		Assert.That(assembly.GetType("DeepFlowTest.Assert.Assertable"), Is.Null);
		Assert.That(
			assembly.GetType("DeepFlowTest.AppDriverPayload.AppDriverPayload")?.GetMethod("Start", BindingFlags.Public | BindingFlags.Static),
			Is.Not.Null);
	}
}
