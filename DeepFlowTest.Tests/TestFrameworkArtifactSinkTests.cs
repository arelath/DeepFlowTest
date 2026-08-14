namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using DeepFlowTest;
using DeepFlowTest.Assert.TestFrameworks;
using NUnit.Framework;

[TestFixture]
public sealed class TestFrameworkArtifactSinkTests
{
	[Test]
	public void XUnitContextReportsTestFailureAndReceivesArtifactsAndDiagnostics()
	{
		var context = new FakeXUnitContext("Tools.Tests.UI.FailingTest", "Failed");
		var sink = new TestFrameworkArtifactSink(() => context);
		var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "xunit-artifact-sink");
		Directory.CreateDirectory(directory);
		var artifactPath = Path.Combine(directory, "final-tree.txt");
		File.WriteAllText(artifactPath, "Window [1] .MainWindow");

		var testContext = sink.GetCurrentTestContext();
		sink.AttachArtifact(artifactPath, "Final visual tree.");
		sink.Log(new AppDriverDiagnostic
		{
			Severity = AppDriverDiagnosticSeverity.Warning,
			Code = "tree-warning",
			Message = "Tree capture warning.",
		});

		Assert.Multiple(() =>
		{
			Assert.That(testContext.TestName, Is.EqualTo("Tools.Tests.UI.FailingTest"));
			Assert.That(testContext.HasFailed, Is.True);
			Assert.That(context.Attachments, Has.Count.EqualTo(1));
			Assert.That(context.Attachments[0].Name, Is.EqualTo("final-tree.txt"));
			Assert.That(context.Attachments[0].MediaType, Is.EqualTo("text/plain"));
			Assert.That(context.Attachments[0].Bytes, Is.EqualTo(File.ReadAllBytes(artifactPath)));
			Assert.That(context.Diagnostics, Has.Some.Contains("tree-warning"));
		});
	}

	[Test]
	public void ExplicitFailureContextSuppliesXUnitV2NameAndFailureState()
	{
		var sink = new TestFrameworkArtifactSink(() => null);

		sink.SetExplicitFailureContext("Tools.Tests.UI.XUnitV2Failure");
		var context = sink.GetCurrentTestContext();

		Assert.Multiple(() =>
		{
			Assert.That(context.TestName, Is.EqualTo("Tools.Tests.UI.XUnitV2Failure"));
			Assert.That(context.HasFailed, Is.True);
		});
	}

	private sealed class FakeXUnitContext(string testDisplayName, string result)
	{
		public FakeXUnitTest Test { get; } = new(testDisplayName);

		public FakeXUnitTestState TestState { get; } = new(result);

		public List<(string Name, byte[] Bytes, string MediaType)> Attachments { get; } = [];

		public List<string> Diagnostics { get; } = [];

		public void AddAttachment(string name, byte[] value, string mediaType) =>
			Attachments.Add((name, value, mediaType));

		public void SendDiagnosticMessage(string message) => Diagnostics.Add(message);
	}

	private sealed class FakeXUnitTest(string testDisplayName)
	{
		public string TestDisplayName { get; } = testDisplayName;
	}

	private sealed class FakeXUnitTestState(string result)
	{
		public string Result { get; } = result;
	}
}
