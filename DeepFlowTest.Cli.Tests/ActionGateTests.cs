namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class ActionGateTests
{
	[Test]
	public void StrictModeDeniesClickWithoutFlag()
	{
		var gate = new ActionGate(name => name == "DEEPFLOWTEST_CLI_STRICT_ACTIONS" ? "true" : null);

		Assert.That(
			() => gate.Demand("click", new CliCommonOptions()),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.ActionDenied));
	}

	[Test]
	public void StrictModeAllowsClickWithFlag()
	{
		var gate = new ActionGate(name => name == "DEEPFLOWTEST_CLI_STRICT_ACTIONS" ? "1" : null);

		Assert.DoesNotThrow(() => gate.Demand("click", new CliCommonOptions { AllowActions = true }));
	}

	[Test]
	public void ArbitraryInvokeRequiresExplicitFlag()
	{
		var gate = new ActionGate(_ => null);

		Assert.That(
			() => gate.Demand("invoke", new CliCommonOptions(), arbitraryInvoke: true),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.ArbitraryInvokeDenied));
	}
}
