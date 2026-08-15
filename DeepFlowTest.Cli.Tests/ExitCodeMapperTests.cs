namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class ExitCodeMapperTests
{
	[TestCase(null, 0)]
	[TestCase(AutomationErrorCodes.InvalidArguments, 1)]
	[TestCase(AutomationErrorCodes.InvalidConfig, 1)]
	[TestCase(AutomationErrorCodes.ActionDenied, 1)]
	[TestCase(AutomationErrorCodes.ArbitraryInvokeDenied, 1)]
	[TestCase(AutomationErrorCodes.TargetNotFound, 2)]
	[TestCase(AutomationErrorCodes.AmbiguousTarget, 2)]
	[TestCase(AutomationErrorCodes.UnsupportedTarget, 3)]
	[TestCase(AutomationErrorCodes.UnsupportedFramework, 3)]
	[TestCase(AutomationErrorCodes.UnsupportedArchitecture, 3)]
	[TestCase(AutomationErrorCodes.AttachFailed, 4)]
	[TestCase(AutomationErrorCodes.PipeFailed, 5)]
	[TestCase(AutomationErrorCodes.ProtocolError, 5)]
	[TestCase(AutomationErrorCodes.CommandTimeout, 6)]
	[TestCase(AutomationErrorCodes.TargetExited, 7)]
	[TestCase(AutomationErrorCodes.NoMatch, 8)]
	[TestCase(AutomationErrorCodes.StaleTarget, 8)]
	[TestCase(AutomationErrorCodes.UnexpectedError, 9)]
	[TestCase(AutomationErrorCodes.PipeBusy, 10)]
	public void MapsDocumentedExitCodes(string? errorCode, int expected)
	{
		Assert.That(ExitCodeMapper.Map(errorCode), Is.EqualTo(expected));
	}
}
