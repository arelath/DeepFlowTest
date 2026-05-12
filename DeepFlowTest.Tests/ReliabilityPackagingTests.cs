namespace DeepFlowTest.Tests;

using System;
using System.IO;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class ReliabilityPackagingTests
{
	[Test]
	public void PipeStatusExposesRuntimeCounters()
	{
		var session = new ReusablePipeSession("test-pipe", _ => { });
		var status = session.CreateStatusResponse();

		Assert.That(status.Counters["commandsHandled"], Is.EqualTo(0));
		Assert.That(status.Counters["activeSubscriptions"], Is.EqualTo(0));
	}

	[Test]
	public void StartupLogTailHandlesMissingFile()
	{
		var found = PayloadLog.TryReadTailForPipe("missing-pipe", 123456, out var tail);

		Assert.That(found, Is.False);
		Assert.That(tail, Is.Empty);
	}

	[Test]
	public void PublishLayoutConfigurationIncludesPayloadAndResourceFolders()
	{
		var project = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "DeepFlowTest.Cli", "DeepFlowTest.Cli.csproj"));

		Assert.That(project, Does.Contain("payloads\\netframework"));
		Assert.That(project, Does.Contain("payloads\\netcoreapp"));
		Assert.That(project, Does.Contain("payloads\\dotnet"));
		Assert.That(project, Does.Contain("DeepFlowTestResources\\x86"));
		Assert.That(project, Does.Contain("DeepFlowTestResources\\x64"));
	}

	[Test]
	public void CiWorkflowDeclaresFastAndPublishLanes()
	{
		var workflow = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".github", "workflows", "ci.yml"));

		Assert.That(workflow, Does.Contain("TestFast"));
		Assert.That(workflow, Does.Contain("PublishCli"));
		Assert.That(workflow, Does.Contain("Pack"));
	}

	[Test]
	public void PerformanceBudgetDocumentExists()
	{
		var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Docs", "PerformanceBudgets.md");

		Assert.That(File.Exists(path), Is.True);
	}
}
