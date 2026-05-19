namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using NUnit.Framework;

[TestFixture]
public sealed class RepositoryConfigurationTests
{
	private static readonly string[] DisallowedProductNames =
	{
		Decode("V3BmUGlsb3Q="),
		Decode("U25vb3A="),
	};

	private static readonly HashSet<string> ExcludedSegments = new(StringComparer.OrdinalIgnoreCase)
	{
		".git",
		".vs",
		"artifacts",
		"bin",
		"Docs",
		"obj",
		"output",
		"packages",
	};

	private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".cmd",
		".cpp",
		".cs",
		".csproj",
		".filters",
		".h",
		".json",
		".props",
		".ps1",
		".rc",
		".sln",
		".targets",
		".vcxproj",
		".xaml",
	};

	private static readonly HashSet<string> SourceFileNames = new(StringComparer.OrdinalIgnoreCase)
	{
		".editorconfig",
		".gitattributes",
		".gitignore",
		".vsconfig",
	};

	[Test]
	public void ProductSourceDoesNotContainPreviousProductNames()
	{
		var root = FindRepositoryRoot();
		var offenders = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Where(IsProductSourceFile)
			.SelectMany(file => FindDisallowedNames(file, File.ReadAllText(file)))
			.ToList();

		Assert.That(offenders, Is.Empty);
	}

	[Test]
	public void CentralPackageManagementIsEnabled()
	{
		var props = XDocument.Load(Path.Combine(FindRepositoryRoot(), "Directory.Build.props"));
		Assert.That(ReadProperty(props, "ManagePackageVersionsCentrally"), Is.EqualTo("true").IgnoreCase);
		Assert.That(ReadProperty(props, "CentralPackageTransitivePinningEnabled"), Is.EqualTo("true").IgnoreCase);
		Assert.That(ReadProperty(props, "DisableWinExeOutputInference"), Is.EqualTo("true").IgnoreCase);
		Assert.That(ReadProperty(props, "ProduceReferenceAssembly"), Is.EqualTo("false").IgnoreCase);
	}

	[Test]
	public void NativeInteropDeclarationsAreCentralized()
	{
		var root = FindRepositoryRoot();
		var disallowedMarkers = new[] { "[" + "DllImport(", "[" + "LibraryImport(" };
		var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(IsProductSourceFile)
			.Where(file => !IsSharedNativeMethodsFile(root, file))
			.SelectMany(file => FindLines(file, disallowedMarkers))
			.ToList();

		Assert.That(offenders, Is.Empty);
	}

	[Test]
	public void BuildScriptDeclaresMilestoneTargets()
	{
		var buildScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".build", "Build.cs"));
		var expectedTargets = new[]
		{
			"Restore",
			"Compile",
			"CompileNativeInjector",
			"RepackPayloads",
			"TestFast",
			"TestCore",
			"TestCli",
			"CompileTestHarnesses",
			"TestIntegration",
			"TestCompat",
			"TestFull",
			"PublishCli",
			"Pack",
			"CI",
		};

		foreach (var target in expectedTargets)
			Assert.That(buildScript, Does.Contain($"\"{target}\""));

		Assert.That(buildScript, Does.Contain("new BuildTarget(\"Compile\", Compile, \"Restore\", \"CompileNativeInjector\")"));
		Assert.That(buildScript, Does.Contain("RepackPayloads();"));
		Assert.That(buildScript, Does.Contain("RunDotNet(\"build\", CliProject"));
	}

	[Test]
	public void ProjectsDeclareExpectedTargetFrameworks()
	{
		var root = FindRepositoryRoot();
		Assert.That(ReadProjectProperty(root, "DeepFlowTest", "DeepFlowTest.csproj", "TargetFrameworks"), Is.EqualTo("net461;netcoreapp3.1;net5.0-windows"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Cli", "DeepFlowTest.Cli.csproj", "TargetFramework"), Is.EqualTo("net8.0-windows"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Recorder", "DeepFlowTest.Recorder.csproj", "TargetFramework"), Is.EqualTo("net8.0-windows"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Tests", "DeepFlowTest.Tests.csproj", "TargetFramework"), Is.EqualTo("net8.0-windows"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Cli.Tests", "DeepFlowTest.Cli.Tests.csproj", "TargetFramework"), Is.EqualTo("net8.0-windows"));
		Assert.That(ReadProjectProperty(root, "TestHarnesses", "Directory.Build.props", "TargetFramework"), Is.EqualTo("net8.0-windows"));
	}

	[Test]
	public void TestHarnessSolutionIncludesRepresentativeWpfAndWinFormsApps()
	{
		var root = FindRepositoryRoot();
		var solution = File.ReadAllText(Path.Combine(root, "TestHarnesses", "TestHarnesses.sln"));
		var basicWindow = File.ReadAllText(Path.Combine(root, "TestHarnesses", "BasicTestHarness", "MainWindow.xaml"));
		var winFormsMain = File.ReadAllText(Path.Combine(root, "TestHarnesses", "WinFormsExampleApp", "MainForm.cs"));
		var winFormsSecondary = File.ReadAllText(Path.Combine(root, "TestHarnesses", "WinFormsExampleApp", "SecondaryForm.cs"));
		var winFormsModal = File.ReadAllText(Path.Combine(root, "TestHarnesses", "WinFormsExampleApp", "ModalDialogForm.cs"));

		Assert.That(solution, Does.Contain("HelloWorld"));
		Assert.That(solution, Does.Contain("BasicTestHarness"));
		Assert.That(solution, Does.Contain("WinFormsExampleApp"));
		Assert.That(basicWindow, Does.Contain("PrimaryButton"));
		Assert.That(basicWindow, Does.Contain("SamplePopup"));
		Assert.That(basicWindow, Does.Contain("ShowModalDialogButton"));
		Assert.That(winFormsMain, Does.Contain("MainForm"));
		Assert.That(winFormsMain, Does.Contain("OpenFileDialog"));
		Assert.That(winFormsSecondary, Does.Contain("SecondaryForm"));
		Assert.That(winFormsModal, Does.Contain("ModalDialogForm"));
	}

	[Test]
	public void PayloadFoldersUseExpectedNamesWhenPresent()
	{
		var root = FindRepositoryRoot();
		var payloadRoot = Path.Combine(root, "output", "payloads");
		if (!Directory.Exists(payloadRoot))
			return;

		var allowedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"netframework",
			"netcoreapp",
			"dotnet",
		};

		var unexpectedFolders = Directory.EnumerateDirectories(payloadRoot)
			.Select(Path.GetFileName)
			.Where(name => name is not null && !allowedFolders.Contains(name))
			.ToList();
		Assert.That(unexpectedFolders, Is.Empty);

		var offenders = Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
			.SelectMany(file => FindDisallowedNames(file, File.ReadAllText(file)))
			.ToList();
		Assert.That(offenders, Is.Empty);
	}

	[Test]
	public void BuildDocumentationMentionsExpectedCommands()
	{
		var root = FindRepositoryRoot();
		var buildDoc = File.ReadAllText(Path.Combine(root, "Docs", "HowToBuildAndTest.md"));
		var payloadDoc = File.ReadAllText(Path.Combine(root, "Docs", "PayloadRepacking.md"));
		var expectedCommands = new[]
		{
			"Restore",
			"Compile",
			"TestFast",
			"CompileTestHarnesses",
			"PublishCli",
			"Pack",
		};

		foreach (var command in expectedCommands)
			Assert.That(buildDoc, Does.Contain(command));

		Assert.That(buildDoc, Does.Contain("fastbuild.ps1"));
		Assert.That(buildDoc, Does.Contain("fasttest.ps1"));
		Assert.That(buildDoc, Does.Contain("Packaging Workflow"));
		Assert.That(payloadDoc, Does.Contain("output/payloads/"));
		Assert.That(payloadDoc, Does.Contain("ILRepack"));
		Assert.That(payloadDoc, Does.Contain("No dependency has an accepted exemption"));
	}

	[Test]
	public void QuickIterationScriptsExist()
	{
		var root = FindRepositoryRoot();
		Assert.That(File.Exists(Path.Combine(root, "fastbuild.ps1")), Is.True);
		Assert.That(File.Exists(Path.Combine(root, "fasttest.ps1")), Is.True);
		Assert.That(File.Exists(Path.Combine(root, "fastbuild.cmd")), Is.True);
		Assert.That(File.Exists(Path.Combine(root, "fasttest.cmd")), Is.True);
	}

	[Test]
	public void InjectorResourcesArePackagedFromArchitectureSpecificFolders()
	{
		var root = FindRepositoryRoot();
		var launcherProject = File.ReadAllText(Path.Combine(root, "DeepFlowTest.InjectorLauncher", "DeepFlowTest.InjectorLauncher.csproj"));
		var nativeProject = File.ReadAllText(Path.Combine(root, "DeepFlowTest.GenericInjector", "DeepFlowTest.GenericInjector.vcxproj"));
		var cliProject = File.ReadAllText(Path.Combine(root, "DeepFlowTest.Cli", "DeepFlowTest.Cli.csproj"));
		var payloadLayoutTargets = File.ReadAllText(Path.Combine(root, "Shared", "DeepFlowTestPayloadLayout.targets"));

		Assert.That(launcherProject, Does.Contain(@"DeepFlowTestResources\$(PlatformTarget)\"));
		Assert.That(nativeProject, Does.Contain(@"DeepFlowTestResources\$(ArchitectureName)\"));
		Assert.That(cliProject, Does.Contain("DeepFlowTestPayloadLayout.targets"));
		Assert.That(payloadLayoutTargets, Does.Contain(@"DeepFlowTestResources\x86"));
		Assert.That(payloadLayoutTargets, Does.Contain(@"DeepFlowTestResources\x64"));
		Assert.That(payloadLayoutTargets, Does.Contain(@"$(PublishDir)DeepFlowTestResources\x86"));
		Assert.That(payloadLayoutTargets, Does.Contain(@"$(PublishDir)DeepFlowTestResources\x64"));
		Assert.That(payloadLayoutTargets, Does.Not.Contain(@"DeepFlowTestResources\x86\**\*.*"));
		Assert.That(payloadLayoutTargets, Does.Not.Contain(@"DeepFlowTestResources\x64\**\*.*"));
		Assert.That(payloadLayoutTargets, Does.Contain(@"DeepFlowTestResources\x86\*.dll"));
		Assert.That(payloadLayoutTargets, Does.Contain(@"DeepFlowTestResources\x64\*.exe"));
		Assert.That(nativeProject, Does.Contain("$(ArchitecturePreprocessorDefinition)"));
		Assert.That(nativeProject, Does.Contain("DEEPFLOWTEST_ARCH_X86"));
	}

	[Test]
	public void BuildPackCreatesLibraryContentLayout()
	{
		var buildScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".build", "Build.cs"));
		var libraryProject = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "DeepFlowTest", "DeepFlowTest.csproj"));

		Assert.That(buildScript, Does.Contain("contentFiles"));
		Assert.That(buildScript, Does.Contain("DeepFlowTestResources"));
		Assert.That(buildScript, Does.Contain("IsPackageResourceFile"));
		Assert.That(buildScript, Does.Not.Contain(".lib"));
		Assert.That(buildScript, Does.Not.Contain(".exp"));
		Assert.That(libraryProject, Does.Contain("BlockIncompleteDirectSdkPack"));
		Assert.That(libraryProject, Does.Contain("DeepFlowTestAllowDirectPack"));
	}

	[Test]
	public void GeneratedBinariesStayOutOfSourceProjectFolders()
	{
		var root = FindRepositoryRoot();
		var sourceFolders = new[]
		{
			"DeepFlowTest",
			"DeepFlowTest.Cli",
			"DeepFlowTest.Cli.Tests",
			"DeepFlowTest.GenericInjector",
			"DeepFlowTest.InjectorLauncher",
			"DeepFlowTest.Recorder",
			"DeepFlowTest.Tests",
			"Shared",
			Path.Combine("TestHarnesses", "HelloWorld"),
			Path.Combine("TestHarnesses", "BasicTestHarness"),
			Path.Combine("TestHarnesses", "WinFormsExampleApp"),
		};

		var generatedFiles = sourceFolders
			.Select(folder => Path.Combine(root, folder))
			.Where(Directory.Exists)
			.SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
			.Where(IsGeneratedBinary)
			.ToList();

		Assert.That(generatedFiles, Is.Empty);
		Assert.That(Directory.Exists(Path.Combine(root, "bin")), Is.True);
	}

	private static IEnumerable<string> FindDisallowedNames(string file, string text)
	{
		foreach (var name in DisallowedProductNames)
		{
			if (text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
				yield return $"{Path.GetRelativePath(FindRepositoryRoot(), file)} contains {name}";
		}
	}

	private static bool IsProductSourceFile(string file)
	{
		if (IsExcluded(file))
			return false;

		var extension = Path.GetExtension(file);
		var fileName = Path.GetFileName(file);
		return SourceExtensions.Contains(extension) || SourceFileNames.Contains(fileName);
	}

	private static bool IsGeneratedBinary(string file)
	{
		if (IsExcluded(file))
			return false;

		var fileName = Path.GetFileName(file);
		var extension = Path.GetExtension(file);
		return extension is ".dll" or ".exe" or ".pdb" ||
			fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
			fileName.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsSharedNativeMethodsFile(string root, string file)
	{
		var relative = Path.GetRelativePath(root, file);
		return relative.StartsWith("Shared" + Path.DirectorySeparatorChar + "NativeMethods", StringComparison.OrdinalIgnoreCase) ||
			relative.StartsWith("Shared" + Path.AltDirectorySeparatorChar + "NativeMethods", StringComparison.OrdinalIgnoreCase);
	}

	private static IEnumerable<string> FindLines(string file, IReadOnlyList<string> markers)
	{
		var lineNumber = 0;
		foreach (var line in File.ReadLines(file))
		{
			lineNumber++;
			if (markers.Any(marker => line.Contains(marker, StringComparison.Ordinal)))
				yield return $"{Path.GetRelativePath(FindRepositoryRoot(), file)}:{lineNumber}";
		}
	}

	private static bool IsExcluded(string path)
	{
		var relative = Path.GetRelativePath(FindRepositoryRoot(), path);
		var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return segments.Any(segment => ExcludedSegments.Contains(segment));
	}

	private static string ReadProjectProperty(string root, params string[] parts)
	{
		var propertyName = parts[^1];
		var projectPath = Path.Combine(parts.Take(parts.Length - 1).Prepend(root).ToArray());
		var document = XDocument.Load(projectPath);
		return ReadProperty(document, propertyName);
	}

	private static string ReadProperty(XDocument document, string propertyName)
	{
		return document.Descendants()
			.Where(element => element.Name.LocalName == propertyName)
			.Select(element => element.Value.Trim())
			.FirstOrDefault() ?? string.Empty;
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
			directory = directory.Parent;

		return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
	}

	private static string Decode(string value)
	{
		return Encoding.UTF8.GetString(Convert.FromBase64String(value));
	}
}
