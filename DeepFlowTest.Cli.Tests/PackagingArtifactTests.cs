namespace DeepFlowTest.Cli.Tests;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

[TestFixture]
public sealed class PackagingArtifactTests
{
	[Test]
	public void ProducedCliLayoutContainsPayloadsAndNativeInjectorConfigs()
	{
		var configuration = Directory.GetParent(TestContext.CurrentContext.TestDirectory)?.Name ?? "Debug";
		var output = Path.Combine(FindRepositoryRoot(), "artifacts", "bin", "DeepFlowTest.Cli", configuration, "net8.0-windows");

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
		Assert.That(File.Exists(Path.Combine(output, "DeepFlowTestResources", "ffmpeg.exe")), Is.False);
	}

	[Test]
	public void ProducedPackageContentFilesLayoutContainsPayloadsAndNativeInjectorConfigs()
	{
		var root = FindRepositoryRoot();
		var packageRoot = Path.Combine(root, "artifacts", "packages", "Debug");
		var version = File.ReadAllText(Path.Combine(root, "version.txt")).Trim();
		var corePackage = Path.Combine(packageRoot, $"DeepFlowTest.{version}.nupkg");
		if (!File.Exists(corePackage))
			Assert.Ignore("Run build.ps1 Pack to produce the package artifact.");

		using var archive = ZipFile.OpenRead(corePackage);
		var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
		Assert.That(entries, Does.Contain("contentFiles/any/any/payloads/netframework/DeepFlowTest.dll"));
		Assert.That(entries, Does.Contain("contentFiles/any/any/payloads/netcoreapp/DeepFlowTest.dll"));
		Assert.That(entries, Does.Contain("contentFiles/any/any/payloads/dotnet/DeepFlowTest.dll"));
		Assert.That(entries, Does.Contain("contentFiles/any/any/DeepFlowTestResources/x86/DeepFlowTest.GenericInjector.x86.dll"));
		Assert.That(entries, Does.Contain("contentFiles/any/any/DeepFlowTestResources/x64/DeepFlowTest.GenericInjector.x64.dll"));
		Assert.That(entries.Any(entry => entry.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)), Is.False);
		Assert.That(entries.Any(entry => entry.EndsWith(".lib", StringComparison.OrdinalIgnoreCase)), Is.False);
		Assert.That(entries.Any(entry => entry.EndsWith(".exp", StringComparison.OrdinalIgnoreCase)), Is.False);

		var nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
		using var nuspecStream = nuspecEntry.Open();
		var nuspec = XDocument.Load(nuspecStream);
		var dependencyGroups = nuspec.Descendants()
			.Single(element => element.Name.LocalName == "dependencies")
			.Elements()
			.Where(element => element.Name.LocalName == "group")
			.Select(element => (string?)element.Attribute("targetFramework"))
			.Where(value => value is not null)
			.ToArray();
		Assert.That(dependencyGroups, Is.EquivalentTo(new[] { ".NETFramework4.6.1", ".NETCoreApp3.1", "net5.0-windows7.0" }));
	}

	[Test]
	public void OptionalMediaPackageOwnsFfmpegAndProvenance()
	{
		var root = FindRepositoryRoot();
		var packageRoot = Path.Combine(root, "artifacts", "packages", "Debug");
		var version = File.ReadAllText(Path.Combine(root, "version.txt")).Trim();
		var mediaPackage = Path.Combine(packageRoot, $"DeepFlowTest.Media.FFmpeg.{version}.nupkg");
		if (!File.Exists(mediaPackage))
			Assert.Ignore("Run build.ps1 Pack to produce the optional media package.");

		using var archive = ZipFile.OpenRead(mediaPackage);
		var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
		Assert.That(entries, Does.Contain("contentFiles/any/any/DeepFlowTestResources/ffmpeg.exe"));
		Assert.That(entries, Does.Contain("provenance/ffmpeg.sha256"));
		Assert.That(entries, Does.Contain("NOTICE.md"));
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
