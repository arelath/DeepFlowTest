namespace DeepFlowTest.Cli.Tests;

using System.IO;
using DeepFlowTest.AppDriverPayload;
using NUnit.Framework;

[TestFixture]
public sealed class SmokeTests
{
	[Test]
	public void ProgramRunReturnsSuccess()
	{
		Assert.That(Program.Run(System.Array.Empty<string>()), Is.EqualTo(0));
	}

	[Test]
	public void ProgramRunRejectsUnknownCommand()
	{
		Assert.That(Program.Run(new[] { "unknown-command" }), Is.EqualTo(1));
	}

	[Test]
	public void CliDiagnosticsCanReadPayloadLogTail()
	{
		const string pipeName = "deepflowtest-cli-diagnostics";
		const int processId = 654321;
		var logPath = PayloadLog.GetLogPath(pipeName, processId);
		Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
		File.WriteAllText(logPath, "payload log tail");
		try
		{
			var found = CliDiagnostics.TryReadPayloadLogTail(pipeName, processId, out var tail);

			Assert.That(found, Is.True);
			Assert.That(tail, Is.EqualTo("payload log tail"));
		}
		finally
		{
			File.Delete(logPath);
		}
	}
}
