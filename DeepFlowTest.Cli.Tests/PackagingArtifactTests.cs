namespace DeepFlowTest.Cli.Tests;

using System;
using System.IO;
using NUnit.Framework;

[TestFixture]
public sealed class PackagingArtifactTests
{
	[Test]
	public void ProducedCliLayoutContainsPayloadsAndNativeInjectorConfigs()
	{
		var output = TestContext.CurrentContext.TestDirectory;

		AssertNonEmptyFile(Path.Combine(output, "DeepFlowTest.Cli.exe"));
		AssertNonEmptyFile(Path.Combine(output, "payloads", "netframework", "DeepFlowTest.dll"));
		AssertNonEmptyFile(Path.Combine(output, "payloads", "netcoreapp", "DeepFlowTest.dll"));
		AssertNonEmptyFile(Path.Combine(output, "payloads", "dotnet", "DeepFlowTest.dll"));
		AssertNonEmptyFile(Path.Combine(output, "DeepFlowTestResources", "x86", "DeepFlowTest.GenericInjector.x86.dll"));
		AssertNonEmptyFile(Path.Combine(output, "DeepFlowTestResources", "x86", "DeepFlowTest.InjectorLauncher.x86.exe"));
		AssertNonEmptyFile(Path.Combine(output, "DeepFlowTestResources", "x86", "DeepFlowTest.InjectorLauncher.x86.exe.config"));
		AssertNonEmptyFile(Path.Combine(output, "DeepFlowTestResources", "x64", "DeepFlowTest.GenericInjector.x64.dll"));
		AssertNonEmptyFile(Path.Combine(output, "DeepFlowTestResources", "x64", "DeepFlowTest.InjectorLauncher.x64.exe"));
		AssertNonEmptyFile(Path.Combine(output, "DeepFlowTestResources", "x64", "DeepFlowTest.InjectorLauncher.x64.exe.config"));
	}

	[Test]
	public void ProducedPackageContentFilesLayoutContainsPayloadsAndNativeInjectorConfigs()
	{
		var root = FindRepositoryRoot();
		var contentRoot = Path.Combine(root, "artifacts", "packages", "Debug", "DeepFlowTest", "contentFiles", "any", "any");
		if (!Directory.Exists(contentRoot))
			Assert.Ignore("Run build.ps1 Pack to produce the package contentFiles artifact.");

		AssertNonEmptyFile(Path.Combine(contentRoot, "payloads", "netframework", "DeepFlowTest.dll"));
		AssertNonEmptyFile(Path.Combine(contentRoot, "payloads", "netcoreapp", "DeepFlowTest.dll"));
		AssertNonEmptyFile(Path.Combine(contentRoot, "payloads", "dotnet", "DeepFlowTest.dll"));
		AssertNonEmptyFile(Path.Combine(contentRoot, "DeepFlowTestResources", "x86", "DeepFlowTest.GenericInjector.x86.dll"));
		AssertNonEmptyFile(Path.Combine(contentRoot, "DeepFlowTestResources", "x86", "DeepFlowTest.InjectorLauncher.x86.exe.config"));
		AssertNonEmptyFile(Path.Combine(contentRoot, "DeepFlowTestResources", "x64", "DeepFlowTest.GenericInjector.x64.dll"));
		AssertNonEmptyFile(Path.Combine(contentRoot, "DeepFlowTestResources", "x64", "DeepFlowTest.InjectorLauncher.x64.exe.config"));
		Assert.That(Directory.GetFiles(contentRoot, "*.lib", SearchOption.AllDirectories), Is.Empty);
		Assert.That(Directory.GetFiles(contentRoot, "*.exp", SearchOption.AllDirectories), Is.Empty);
	}

	private static void AssertNonEmptyFile(string path)
	{
		Assert.That(File.Exists(path), Is.True, path);
		Assert.That(new FileInfo(path).Length, Is.GreaterThan(0), path);
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
			directory = directory.Parent;

		return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
	}
}
