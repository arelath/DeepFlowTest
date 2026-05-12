namespace DeepFlowTest.Tests;

using System;
using System.IO;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.InjectorLauncher;
using NUnit.Framework;

[TestFixture]
public sealed class PayloadLoggingTests
{
	[Test]
	public void InjectorCanLocatePayloadLogTailFromStartupArgument()
	{
		var processId = 123456;
		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = $"deepflowtest-test-{Guid.NewGuid():N}",
			Mode = PayloadStartupModes.OneShotDriver,
			PayloadRoot = AppContext.BaseDirectory,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};
		var logPath = PayloadLogLocator.GetLogPath(options.PipeName, processId);
		Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
		File.WriteAllText(logPath, "first\nsecond\nthird");
		try
		{
			var found = PayloadLogLocator.TryReadTail(options.Encode(), processId, out var tail, maxCharacters: 12);

			Assert.That(found, Is.True);
			Assert.That(tail, Does.Contain("third"));
			Assert.That(tail.Length, Is.LessThanOrEqualTo(12));
		}
		finally
		{
			File.Delete(logPath);
		}
	}

	[Test]
	public void InjectorFallsBackToStartupLogWhenStartupArgumentCannotBeParsed()
	{
		var processId = 123457;
		var logPath = PayloadLogLocator.GetLogPath("startup", processId);
		Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
		File.WriteAllText(logPath, "startup parse failed");
		try
		{
			var found = PayloadLogLocator.TryReadTail("dft:not-base64", processId, out var tail);

			Assert.That(found, Is.True);
			Assert.That(tail, Does.Contain("startup parse failed"));
		}
		finally
		{
			File.Delete(logPath);
		}
	}
}
