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
		var root = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..");
		var project = File.ReadAllText(Path.Combine(root, "DeepFlowTest.Cli", "DeepFlowTest.Cli.csproj"));
		var payloadLayoutTargets = File.ReadAllText(Path.Combine(root, "Shared", "DeepFlowTestPayloadLayout.targets"));

		Assert.That(project, Does.Contain("DeepFlowTestPayloadLayout.targets"));
		Assert.That(payloadLayoutTargets, Does.Contain("payloads\\netframework"));
		Assert.That(payloadLayoutTargets, Does.Contain("payloads\\netcoreapp"));
		Assert.That(payloadLayoutTargets, Does.Contain("payloads\\dotnet"));
		Assert.That(payloadLayoutTargets, Does.Contain("DeepFlowTestResources\\x86"));
		Assert.That(payloadLayoutTargets, Does.Contain("DeepFlowTestResources\\x64"));
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
