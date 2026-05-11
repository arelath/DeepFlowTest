namespace DeepFlowTest.Tests;

using System.IO;
using NUnit.Framework;

[TestFixture]
public sealed class NativeInjectorProjectTests
{
	[Test]
	public void NativeProjectDeclaresExpectedOutputsAndExport()
	{
		var root = FindRepositoryRoot();
		var project = File.ReadAllText(Path.Combine(root, "DeepFlowTest.GenericInjector", "DeepFlowTest.GenericInjector.vcxproj"));
		var executor = File.ReadAllText(Path.Combine(root, "DeepFlowTest.GenericInjector", "Executor.cpp"));

		Assert.That(project, Does.Contain("DeepFlowTest.GenericInjector.$(ArchitectureName)"));
		Assert.That(project, Does.Contain("version.rc"));
		Assert.That(executor, Does.Contain("ExecuteInDefaultAppDomain"));
		Assert.That(executor, Does.Contain("netframework"));
		Assert.That(executor, Does.Contain("netcoreapp"));
		Assert.That(executor, Does.Contain("dotnet"));
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
			directory = directory.Parent;

		return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
	}
}
