namespace DeepFlowTest.Tests;

using System.IO;
using NUnit.Framework;

[TestFixture]
public sealed class DocumentationCompatibilityTests
{
	[TestCase("Compatibility.md", "namespace", "command is additive", "DeepFlowTestResources")]
	[TestCase("Recording.md", "Record", "Semantic recording", "Screenshot streaming")]
	[TestCase("Win32DialogSupport.md", "Dialog", "FileName", "AcceptDialog")]
	[TestCase("WinFormsSupport.md", "WinForms", "secondary forms", "modal dialogs")]
	[TestCase("Protocol.md", "StreamMessage", "SequenceNumber", "event-log")]
	[TestCase(@"CLIDesign\README.md", "config get", "version", "DEEPFLOWTEST_CLI_STRICT_ACTIONS")]
	[TestCase(@"CLIDesign\LLMAgentUsage.md", "stale-target", "visual-tree-delta", "JSON envelopes")]
	public void CompatibilityDocumentationCoversPortedLegacyTopics(string relativePath, string first, string second, string third)
	{
		var path = Path.Combine(FindRepositoryRoot(), "Docs", relativePath);

		Assert.That(File.Exists(path), Is.True, path);
		var text = File.ReadAllText(path);
		Assert.That(text, Does.Contain(first));
		Assert.That(text, Does.Contain(second));
		Assert.That(text, Does.Contain(third));
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
			directory = directory.Parent;

		return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
	}
}
