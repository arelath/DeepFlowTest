namespace DeepFlowTest.Cli.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class ExitCodeMapperTests
{
	[TestCase(null, 0)]
	[TestCase(CliErrorCodes.InvalidArguments, 1)]
	[TestCase(CliErrorCodes.InvalidConfig, 1)]
	[TestCase(CliErrorCodes.ActionDenied, 1)]
	[TestCase(CliErrorCodes.ArbitraryInvokeDenied, 1)]
	[TestCase(CliErrorCodes.TargetNotFound, 2)]
	[TestCase(CliErrorCodes.AmbiguousTarget, 2)]
	[TestCase(CliErrorCodes.UnsupportedTarget, 3)]
	[TestCase(CliErrorCodes.UnsupportedFramework, 3)]
	[TestCase(CliErrorCodes.UnsupportedArchitecture, 3)]
	[TestCase(CliErrorCodes.AttachFailed, 4)]
	[TestCase(CliErrorCodes.PipeFailed, 5)]
	[TestCase(CliErrorCodes.ProtocolError, 5)]
	[TestCase(CliErrorCodes.CommandTimeout, 6)]
	[TestCase(CliErrorCodes.TargetExited, 7)]
	[TestCase(CliErrorCodes.NoMatch, 8)]
	[TestCase(CliErrorCodes.StaleTarget, 8)]
	[TestCase(CliErrorCodes.UnexpectedError, 9)]
	[TestCase(CliErrorCodes.PipeBusy, 10)]
	public void MapsDocumentedExitCodes(string? errorCode, int expected)
	{
		Assert.That(ExitCodeMapper.Map(errorCode), Is.EqualTo(expected));
	}
}
