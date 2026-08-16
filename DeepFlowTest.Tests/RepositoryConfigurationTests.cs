namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
		Assert.That(ReadProperty(props, "BaseOutputPath"), Is.EqualTo(@"$(ArtifactsBinRoot)$(MSBuildProjectName)\"));
		Assert.That(ReadProperty(props, "BaseIntermediateOutputPath"), Is.EqualTo(@"$(ArtifactsObjRoot)$(MSBuildProjectName)\"));
		Assert.That(ReadProperty(props, "PackageOutputPath"), Is.EqualTo(@"$(ArtifactsPackagesRoot)$(Configuration)\"));
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
			"TestMcp",
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
		Assert.That(buildScript, Does.Contain("new BuildTarget(\"TestFast\", TestFast, \"Compile\")"));
		Assert.That(buildScript, Does.Contain("new BuildTarget(\"TestMcp\", TestMcp, \"Restore\")"));
		Assert.That(buildScript, Does.Contain("RepackPayloads();"));
		Assert.That(buildScript, Does.Contain("RunDotNet(\"build\", CliProject"));
	}

	[Test]
	public void FastTestLaneRunsEveryUnitTestProject()
	{
		var buildScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".build", "Build.cs"));
		var testFastStart = buildScript.IndexOf("private void TestFast()", StringComparison.Ordinal);
		var testFastEnd = buildScript.IndexOf("private void TestCore()", testFastStart, StringComparison.Ordinal);
		var testFast = buildScript.Substring(testFastStart, testFastEnd - testFastStart);

		Assert.That(testFast, Does.Contain("RunDotNetTest(CoreTestsProject"));
		Assert.That(testFast, Does.Contain("RunDotNetTest(PayloadTestsProject"));
		Assert.That(testFast, Does.Contain("RunDotNetTest(CliTestsProject"));
		Assert.That(testFast, Does.Contain("RunDotNetTest(McpTestsProject"));
		Assert.That(testFast, Does.Contain("\"--no-build\""));
		Assert.That(testFast, Does.Contain("\"--blame-hang\""));
		Assert.That(testFast, Does.Contain("\"--blame-crash\""));
		Assert.That(buildScript, Does.Contain("process.Kill(entireProcessTree: true)"));
		Assert.That(buildScript, Does.Contain("timed out after"));
		Assert.That(buildScript, Does.Contain("private string McpTestsProject => Path.Combine(rootDirectory, \"DeepFlowTest.Mcp.Tests\", \"DeepFlowTest.Mcp.Tests.csproj\")"));
	}

	[Test]
	public void SupportedBuildScriptsSerializeSharedWorkspaceArtifacts()
	{
		var root = FindRepositoryRoot();
		var helperPath = Path.Combine(root, "Tools", "WorkspaceBuildLock.ps1");
		Assert.That(File.Exists(helperPath), Is.True);
		var helper = File.ReadAllText(helperPath);
		Assert.That(helper, Does.Contain("FileShare]::None"));
		Assert.That(helper, Does.Contain(".workspace-build-owner.json"));

		foreach (var scriptName in new[] { "build.ps1", "fastbuild.ps1", "fasttest.ps1" })
		{
			var script = File.ReadAllText(Path.Combine(root, scriptName));
			Assert.That(script, Does.Contain("WorkspaceBuildLock.ps1"), scriptName);
			Assert.That(script, Does.Contain("Enter-WorkspaceBuildLock"), scriptName);
			Assert.That(script, Does.Contain("Exit-WorkspaceBuildLock"), scriptName);
			Assert.That(script, Does.Contain("finally"), scriptName);
		}
	}

	[Test]
	public void WorkspaceBuildLockReleasesHandleWhenMetadataPublicationFails()
	{
		var root = FindRepositoryRoot();
		var helperPath = Path.Combine(root, "Tools", "WorkspaceBuildLock.ps1");
		var temporaryRoot = Path.Combine(Path.GetTempPath(), $"DeepFlowTest-lock-{Guid.NewGuid():N}");
		Directory.CreateDirectory(temporaryRoot);

		try
		{
			var script = $$"""
				$ErrorActionPreference = 'Stop'
				. '{{EscapePowerShellLiteral(helperPath)}}'
				function global:Move-Item {
					param([string]$LiteralPath, [string]$Destination, [switch]$Force)
					throw [System.IO.IOException]::new('Forced metadata publication failure.')
				}
				$failedAsExpected = $false
				try {
					Enter-WorkspaceBuildLock -Root '{{EscapePowerShellLiteral(temporaryRoot)}}' -Timeout ([TimeSpan]::FromSeconds(2)) -CommandDescription 'metadata failure test' | Out-Null
				}
				catch [System.IO.IOException] {
					$failedAsExpected = $true
				}
				Microsoft.PowerShell.Management\Remove-Item -LiteralPath function:\Move-Item
				if (-not $failedAsExpected) { throw 'Metadata publication unexpectedly succeeded.' }
				$lockPath = Join-Path '{{EscapePowerShellLiteral(temporaryRoot)}}' 'artifacts/.workspace-build.lock'
				$probe = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
				$probe.Dispose()
				Write-Output 'LOCK_RELEASED'
				""";
			var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
			var startInfo = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encodedCommand}")
			{
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
			};

			using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
			var standardOutput = process.StandardOutput.ReadToEnd();
			var standardError = process.StandardError.ReadToEnd();
			if (!process.WaitForExit(30_000))
			{
				process.Kill(entireProcessTree: true);
				Assert.Fail("PowerShell lock regression test timed out.");
			}

			Assert.That(process.ExitCode, Is.Zero, standardError);
			Assert.That(standardOutput, Does.Contain("LOCK_RELEASED"), standardError);
		}
		finally
		{
			Directory.Delete(temporaryRoot, recursive: true);
		}
	}

	[Test]
	public void ProjectsDeclareExpectedTargetFrameworks()
	{
		var root = FindRepositoryRoot();
		Assert.That(ReadProjectProperty(root, "Shared", "DeepFlowTest.Frameworks.props", "DeepFlowTestTargetFrameworks"), Is.EqualTo("net461;netcoreapp3.1;net5.0-windows"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest", "DeepFlowTest.csproj", "TargetFrameworks"), Is.EqualTo("$(DeepFlowTestTargetFrameworks)"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Payload", "DeepFlowTest.Payload.csproj", "TargetFrameworks"), Is.EqualTo("$(DeepFlowTestTargetFrameworks)"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Automation", "DeepFlowTest.Automation.csproj", "TargetFramework"), Is.EqualTo("net8.0-windows"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Cli", "DeepFlowTest.Cli.csproj", "TargetFramework"), Is.EqualTo("net8.0-windows"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Recorder", "DeepFlowTest.Recorder.csproj", "TargetFramework"), Is.EqualTo("net8.0-windows"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Tests", "DeepFlowTest.Tests.csproj", "TargetFramework"), Is.EqualTo("net8.0-windows"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Cli.Tests", "DeepFlowTest.Cli.Tests.csproj", "TargetFramework"), Is.EqualTo("net8.0-windows"));
		Assert.That(ReadProjectProperty(root, "DeepFlowTest.Mcp.Tests", "DeepFlowTest.Mcp.Tests.csproj", "TargetFramework"), Is.EqualTo("net8.0-windows"));
		Assert.That(ReadProjectProperty(root, "TestHarnesses", "Directory.Build.props", "TargetFramework"), Is.EqualTo("net8.0-windows"));
	}

	[Test]
	public void McpDependsOnAutomationInsteadOfCli()
	{
		var root = FindRepositoryRoot();
		var mcpProject = File.ReadAllText(Path.Combine(root, "DeepFlowTest.Mcp", "DeepFlowTest.Mcp.csproj"));
		var automationProject = File.ReadAllText(Path.Combine(root, "DeepFlowTest.Automation", "DeepFlowTest.Automation.csproj"));

		Assert.Multiple(() =>
		{
			Assert.That(mcpProject, Does.Contain(@"..\DeepFlowTest.Automation\DeepFlowTest.Automation.csproj"));
			Assert.That(mcpProject, Does.Not.Contain(@"..\DeepFlowTest.Cli\DeepFlowTest.Cli.csproj"));
			Assert.That(automationProject, Does.Not.Contain("DeepFlowTest.Cli"));
		});

		var offenders = new[] { "DeepFlowTest.Mcp", "DeepFlowTest.Mcp.Tests" }
			.Select(directory => Path.Combine(root, directory))
			.SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
			.Where(file => !IsExcluded(file))
			.Where(file => File.ReadAllText(file).Contains("DeepFlowTest.Cli", StringComparison.Ordinal))
			.Select(file => Path.GetRelativePath(root, file))
			.ToArray();

		Assert.That(offenders, Is.Empty, "MCP source must not import or qualify CLI adapter types.");
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
		var payloadRoot = Path.Combine(root, "artifacts", "staging", "payloads");
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
		var readme = File.ReadAllText(Path.Combine(root, "README.md"));
		var buildDoc = File.ReadAllText(Path.Combine(root, "Docs", "HowToBuildAndTest.md"));
		var payloadDoc = File.ReadAllText(Path.Combine(root, "Docs", "PayloadRepacking.md"));
		var expectedCommands = new[]
		{
			"Restore",
			"Compile",
			"TestFast",
			"TestMcp",
			"CompileTestHarnesses",
			"PublishCli",
			"Pack",
		};

		foreach (var command in expectedCommands)
			Assert.That(buildDoc, Does.Contain(command));

		Assert.That(buildDoc, Does.Contain("fastbuild.ps1"));
		Assert.That(buildDoc, Does.Contain("fasttest.ps1"));
		Assert.That(buildDoc, Does.Contain("NoTestRecordings"));
		Assert.That(buildDoc, Does.Contain("DeepFlowTestTestRecordings"));
		Assert.That(readme, Does.Contain("no-test-recordings"));
		Assert.That(buildDoc, Does.Contain("Packaging Workflow"));
		Assert.That(payloadDoc, Does.Contain("artifacts/staging/payloads/"));
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
	public void QuickIterationScriptsIncludePayloadAndMcpAliases()
	{
		var root = FindRepositoryRoot();
		var fastTest = File.ReadAllText(Path.Combine(root, "fasttest.ps1"));
		var fastBuild = File.ReadAllText(Path.Combine(root, "fastbuild.ps1"));

		Assert.That(fastTest, Does.Contain("\"payload\" = @{ Project = \"DeepFlowTest.Payload.Tests\\DeepFlowTest.Payload.Tests.csproj\""));
		Assert.That(fastTest, Does.Contain("\"payload-tests\" = @{ Project = \"DeepFlowTest.Payload.Tests\\DeepFlowTest.Payload.Tests.csproj\""));
		Assert.That(fastTest, Does.Contain("\"mcp\" = @{ Project = \"DeepFlowTest.Mcp.Tests\\DeepFlowTest.Mcp.Tests.csproj\""));
		Assert.That(fastTest, Does.Contain("\"mcp-tests\" = @{ Project = \"DeepFlowTest.Mcp.Tests\\DeepFlowTest.Mcp.Tests.csproj\""));
		Assert.That(fastBuild, Does.Contain("\"mcp\" = @{ Project = \"DeepFlowTest.Mcp\\DeepFlowTest.Mcp.csproj\""));
	}

	[Test]
	public void InjectorResourcesArePackagedFromArchitectureSpecificFolders()
	{
		var root = FindRepositoryRoot();
		var launcherProject = File.ReadAllText(Path.Combine(root, "DeepFlowTest.InjectorLauncher", "DeepFlowTest.InjectorLauncher.csproj"));
		var nativeProject = File.ReadAllText(Path.Combine(root, "DeepFlowTest.GenericInjector", "DeepFlowTest.GenericInjector.vcxproj"));
		var cliProject = File.ReadAllText(Path.Combine(root, "DeepFlowTest.Cli", "DeepFlowTest.Cli.csproj"));
		var solution = File.ReadAllText(Path.Combine(root, "DeepFlowTest.sln"));
		var payloadLayoutTargets = File.ReadAllText(Path.Combine(root, "Shared", "DeepFlowTestPayloadLayout.targets"));

		Assert.That(launcherProject, Does.Contain("$(ArtifactsBinRoot)"));
		Assert.That(launcherProject, Does.Contain("StageInjectorLauncher"));
		Assert.That(launcherProject, Does.Contain("CompileX86InjectorLauncher"));
		Assert.That(launcherProject, Does.Contain("CompileX64InjectorLauncher"));
		Assert.That(nativeProject, Does.Contain("$(ArtifactsRoot)bin"));
		Assert.That(nativeProject, Does.Contain("StageGenericInjector"));
		Assert.That(nativeProject, Does.Contain("CompileX86GenericInjector"));
		Assert.That(nativeProject, Does.Contain("CompileX64GenericInjector"));
		Assert.That(cliProject, Does.Contain("DeepFlowTestPayloadLayout.targets"));
		Assert.That(solution, Does.Contain("ProjectDependencies"));
		Assert.That(solution, Does.Contain("{126C2986-2493-4C81-9A8E-4E5E620AE10F} = {126C2986-2493-4C81-9A8E-4E5E620AE10F}"));
		Assert.That(solution, Does.Contain("{BF1982E4-0690-47C2-9000-EA2AB9A4E8C5} = {BF1982E4-0690-47C2-9000-EA2AB9A4E8C5}"));
		Assert.That(solution, Does.Contain("{BF1982E4-0690-47C2-9000-EA2AB9A4E8C5}.Debug|Any CPU.Build.0 = Debug|Win32"));
		Assert.That(solution, Does.Contain("{BF1982E4-0690-47C2-9000-EA2AB9A4E8C5}.Release|Any CPU.Build.0 = Release|Win32"));
		Assert.That(payloadLayoutTargets, Does.Contain(@"$(ArtifactsStagingRoot)payloads\**\*.*"));
		Assert.That(payloadLayoutTargets, Does.Contain(@"$(ArtifactsStagingRoot)DeepFlowTestResources\**\*.dll"));
		Assert.That(payloadLayoutTargets, Does.Contain(@"$(PublishDir)DeepFlowTestResources\%(RecursiveDir)"));
		Assert.That(nativeProject, Does.Contain("$(ArchitecturePreprocessorDefinition)"));
		Assert.That(nativeProject, Does.Contain("DEEPFLOWTEST_ARCH_X86"));
	}

	[Test]
	public void BuildPackCreatesLibraryContentLayout()
	{
		var buildScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".build", "Build.cs"));
		var libraryProject = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "DeepFlowTest", "DeepFlowTest.csproj"));

		Assert.That(buildScript, Does.Contain("RunDotNet(\"pack\", ClientProject"));
		Assert.That(buildScript, Does.Contain("RunDotNet(\"pack\", MediaPackageProject"));
		Assert.That(buildScript, Does.Not.Contain("WritePackageNuspec"));
		Assert.That(buildScript, Does.Not.Contain("Directory.Packages.props"));
		Assert.That(libraryProject, Does.Contain("$(ArtifactsStagingRoot)payloads"));
		Assert.That(libraryProject, Does.Contain("PackageCopyToOutput=\"true\""));
		Assert.That(libraryProject, Does.Not.Contain("ffmpeg.exe"));
		Assert.That(libraryProject, Does.Not.Contain("BlockIncompleteDirectSdkPack"));
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
		Assert.That(Directory.Exists(Path.Combine(root, "artifacts", "bin")), Is.True);
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

	private static string EscapePowerShellLiteral(string value)
	{
		return value.Replace("'", "''", StringComparison.Ordinal);
	}

	private static string Decode(string value)
	{
		return Encoding.UTF8.GetString(Convert.FromBase64String(value));
	}
}
