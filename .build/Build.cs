namespace DeepFlowTest.Build;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

internal sealed class Build
{
	private readonly string configuration;
	private readonly string dotnet;
	private readonly IReadOnlyList<string> requestedTargets;
	private readonly string rootDirectory;
	private readonly Dictionary<string, BuildTarget> targets;
	private readonly HashSet<string> visitedTargets = new(StringComparer.OrdinalIgnoreCase);

	private Build(string[] args)
	{
		var options = Parse(args);
		configuration = options.Configuration;
		requestedTargets = options.Targets;
		dotnet = Environment.GetEnvironmentVariable("DOTNET_EXE") ?? "dotnet";
		rootDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
		targets = CreateTargets();
	}

	public static int Main(string[] args)
	{
		try
		{
			return new Build(args).Run();
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 1;
		}
	}

	private int Run()
	{
		foreach (var target in requestedTargets)
			RunTarget(target);

		return 0;
	}

	private Dictionary<string, BuildTarget> CreateTargets()
	{
		var declaredTargets = new[]
		{
			new BuildTarget("Restore", Restore),
			new BuildTarget("CompileNativeInjector", CompileNativeInjector),
			new BuildTarget("RepackPayloads", RepackPayloads),
			new BuildTarget("Compile", Compile, "Restore", "CompileNativeInjector"),
			new BuildTarget("TestFast", TestFast, "Restore"),
			new BuildTarget("TestCore", TestCore, "Restore"),
			new BuildTarget("TestCli", TestCli, "Restore"),
			new BuildTarget("CompileTestHarnesses", CompileTestHarnesses, "Restore"),
			new BuildTarget("TestIntegration", TestIntegration, "CompileTestHarnesses"),
			new BuildTarget("TestCompat", TestCompat, "Compile"),
			new BuildTarget("TestFull", TestFull, "TestFast", "TestIntegration", "TestCompat"),
			new BuildTarget("PublishCli", PublishCli, "Compile"),
			new BuildTarget("Pack", Pack, "Compile"),
			new BuildTarget("Publish", Publish, "PublishCli", "Pack"),
			new BuildTarget("CI", CI, "Compile", "TestFast", "PublishCli", "Pack"),
		};

		return declaredTargets.ToDictionary(static target => target.Name, StringComparer.OrdinalIgnoreCase);
	}

	private void RunTarget(string name)
	{
		if (visitedTargets.Contains(name))
			return;

		if (!targets.TryGetValue(name, out var target))
			throw new InvalidOperationException($"Unknown build target '{name}'.");

		foreach (var dependency in target.Dependencies)
			RunTarget(dependency);

		Console.WriteLine($"==> {target.Name}");
		target.Action();
		visitedTargets.Add(target.Name);
	}

	private void Restore()
	{
		RunDotNet("restore", MainSolution);
		RunDotNet("restore", HarnessSolution);
	}

	private void Compile()
	{
		RunDotNet("build", MainSolution, "--configuration", configuration, "--no-restore", "/p:RootBuild=true");
		RepackPayloads();
		RunDotNet("build", CliProject, "--configuration", configuration, "--no-restore", "/p:RootBuild=true");
	}

	private void CompileNativeInjector()
	{
		var msbuild = FindMsBuild();
		RunProcess(msbuild, NativeInjectorProject, "/t:Build", $"/p:Configuration={configuration}", "/p:Platform=Win32", "/m", "/nologo");
		RunProcess(msbuild, NativeInjectorProject, "/t:Build", $"/p:Configuration={configuration}", "/p:Platform=x64", "/m", "/nologo");
	}

	private void RepackPayloads()
	{
		var payloadRoot = Path.Combine(rootDirectory, "output", "payloads");
		Directory.CreateDirectory(payloadRoot);

		var mappings = new[]
		{
			new PayloadMapping("net461", "netframework"),
			new PayloadMapping("netcoreapp3.1", "netcoreapp"),
			new PayloadMapping("net5.0-windows", "dotnet"),
		};

		foreach (var mapping in mappings)
		{
			var source = Path.Combine(rootDirectory, "bin", configuration, mapping.TargetFramework);
			var destination = Path.Combine(payloadRoot, mapping.PayloadFamily);

			if (!Directory.Exists(source))
				throw new DirectoryNotFoundException($"Expected payload build output '{source}' was not found.");

			ResetDirectory(destination, payloadRoot);
			Directory.CreateDirectory(destination);

			var primaryAssembly = Path.Combine(source, "DeepFlowTest.dll");
			if (!File.Exists(primaryAssembly))
				throw new FileNotFoundException("Primary payload assembly was not found.", primaryAssembly);

			var dependencies = GetPayloadDependencies(source).ToArray();
			var outputAssembly = Path.Combine(destination, "DeepFlowTest.dll");
			RunILRepack(primaryAssembly, dependencies, outputAssembly);
			CopyIfExists(Path.Combine(source, "DeepFlowTest.pdb"), Path.Combine(destination, "DeepFlowTest.pdb"));
			CopyIfExists(Path.Combine(source, "DeepFlowTest.xml"), Path.Combine(destination, "DeepFlowTest.xml"));
			WritePayloadManifest(destination, mapping, dependencies);
		}

		File.WriteAllText(
			Path.Combine(payloadRoot, "REPACKING.md"),
			"Payload assemblies are generated with ILRepack and internalize payload third-party dependencies. Loose third-party payload DLLs are not expected in framework-family folders." + Environment.NewLine);
	}

	private void RunILRepack(string primaryAssembly, IReadOnlyList<string> dependencies, string outputAssembly)
	{
		var project = Path.Combine(rootDirectory, ".build", "PayloadRepack.proj");
		var packageVersion = ReadCentralPackageVersion("ILRepack.Lib.MSBuild.Task");
		var dependencyListFile = Path.Combine(rootDirectory, "output", "repack", $"{Path.GetFileName(Path.GetDirectoryName(outputAssembly))}-dependencies.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(dependencyListFile)!);
		File.WriteAllLines(dependencyListFile, dependencies);
		RunProcess(
			FindMsBuild(),
			project,
			"/t:Repack",
			"/nologo",
			$"/p:ILRepackVersion={packageVersion}",
			$"/p:PrimaryAssembly={primaryAssembly}",
			$"/p:DependencyAssemblyListFile={dependencyListFile}",
			$"/p:OutputFile={outputAssembly}");
	}

	private IEnumerable<string> GetPayloadDependencies(string sourceDirectory)
	{
		var dependencyNames = new[]
		{
			"Newtonsoft.Json.dll",
			"Serialize.Linq.dll",
			"0Harmony.dll",
			"System.Buffers.dll",
			"System.Memory.dll",
			"System.Numerics.Vectors.dll",
			"System.Runtime.CompilerServices.Unsafe.dll",
			"System.ValueTuple.dll",
		};

		foreach (var dependencyName in dependencyNames)
		{
			var path = Path.Combine(sourceDirectory, dependencyName);
			if (File.Exists(path))
				yield return path;
		}
	}

	private static void ResetDirectory(string directory, string expectedRoot)
	{
		var fullDirectory = Path.GetFullPath(directory);
		var fullRoot = Path.GetFullPath(expectedRoot);
		if (!fullDirectory.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException($"Refusing to reset directory outside payload root: {fullDirectory}");

		if (Directory.Exists(fullDirectory))
			Directory.Delete(fullDirectory, recursive: true);
	}

	private static void CopyIfExists(string source, string destination)
	{
		if (!File.Exists(source))
			return;

		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
		File.Copy(source, destination, overwrite: true);
	}

	private static void WritePayloadManifest(string destination, PayloadMapping mapping, IReadOnlyList<string> dependencies)
	{
		var lines = new List<string>
		{
			"# DeepFlowTest Payload",
			string.Empty,
			$"- targetFramework: {mapping.TargetFramework}",
			$"- payloadFamily: {mapping.PayloadFamily}",
			$"- repacker: ILRepack",
			"- internalizedDependencies:",
		};
		lines.AddRange(dependencies.Select(path => $"  - {Path.GetFileName(path)}"));
		File.WriteAllLines(Path.Combine(destination, "DeepFlowTest.payload.md"), lines);
	}

	private void TestFast()
	{
		RunDotNet("test", CoreTestsProject, "--configuration", configuration, "--no-restore");
		RunDotNet("test", CliTestsProject, "--configuration", configuration, "--no-restore");
	}

	private void TestCore()
	{
		RunDotNet("test", CoreTestsProject, "--configuration", configuration, "--no-restore");
	}

	private void TestCli()
	{
		RunDotNet("test", CliTestsProject, "--configuration", configuration, "--no-restore");
	}

	private void CompileTestHarnesses()
	{
		RunDotNet("build", HarnessSolution, "--configuration", configuration, "--no-restore");
	}

	private static void TestIntegration()
	{
		Console.WriteLine("Integration lane is declared for later harness-launching tests.");
	}

	private static void TestCompat()
	{
		Console.WriteLine("Compatibility lane is declared for later target framework and architecture matrix tests.");
	}

	private static void TestFull()
	{
		Console.WriteLine("Full lane completed declared dependencies.");
	}

	private void PublishCli()
	{
		RunDotNet("publish", CliProject, "--configuration", configuration, "--no-restore");
	}

	private void Pack()
	{
		var artifactsRoot = Path.Combine(rootDirectory, "artifacts", "packages", configuration);
		var packageDirectory = Path.Combine(artifactsRoot, "DeepFlowTest");
		ResetDirectory(packageDirectory, artifactsRoot);
		Directory.CreateDirectory(packageDirectory);

		var contentRoot = Path.Combine(packageDirectory, "contentFiles", "any", "any");
		CopyDirectory(Path.Combine(rootDirectory, "output", "payloads"), Path.Combine(contentRoot, "payloads"));
		CopyDirectory(
			Path.Combine(rootDirectory, "bin", configuration, "DeepFlowTestResources"),
			Path.Combine(contentRoot, "DeepFlowTestResources"),
			IsPackageResourceFile);
		CopyDirectoryIfExists(
			Path.Combine(rootDirectory, "DeepFlowTest", "contentFiles", "any", "any"),
			contentRoot,
			IsPackageResourceFile);
		CopyDirectoryIfExists(
			Path.Combine(rootDirectory, "Tools", "DeepFlowTestResources"),
			Path.Combine(contentRoot, "DeepFlowTestResources"),
			IsPackageResourceFile);

		foreach (var targetFramework in LibraryTargetFrameworks)
			StageLibraryCompileAssemblies(packageDirectory, targetFramework);

		WritePackageBuildTargets(packageDirectory);

		File.WriteAllText(
			Path.Combine(packageDirectory, "DeepFlowTest.package.md"),
			"DeepFlowTest package layout includes framework-family payload folders and architecture-specific injector resources under contentFiles/any/any." + Environment.NewLine);

		var packageVersion = ResolvePackageVersion();
		var nuspecPath = WritePackageNuspec(packageDirectory, packageVersion);
		var nupkgPath = BuildNupkg(packageDirectory, nuspecPath, artifactsRoot, packageVersion);

		Console.WriteLine($"Package content layout prepared at {packageDirectory}.");
		Console.WriteLine($"NuGet package produced at {nupkgPath} ({new FileInfo(nupkgPath).Length:N0} bytes).");
	}

	private string BuildNupkg(string packageDirectory, string nuspecPath, string artifactsRoot, string packageVersion)
	{
		var packagingProject = Path.Combine(rootDirectory, ".build", "Packaging.proj");
		RunDotNet(
			"pack",
			packagingProject,
			"--configuration",
			configuration,
			$"/p:NuspecFile={nuspecPath}",
			$"/p:NuspecBasePath={packageDirectory}",
			$"/p:PackageOutputPath={artifactsRoot}",
			$"/p:PackageVersion={packageVersion}",
			"/p:IncludeBuildOutput=false",
			"--nologo");

		var nupkgPath = Path.Combine(artifactsRoot, $"DeepFlowTest.{packageVersion}.nupkg");
		if (!File.Exists(nupkgPath))
			throw new FileNotFoundException("Expected nupkg was not produced.", nupkgPath);

		return nupkgPath;
	}

	private void Publish()
	{
		Console.WriteLine("Publish lane produced CLI artifacts and NuGet package.");
	}

	private void StageLibraryCompileAssemblies(string packageDirectory, string targetFramework)
	{
		var source = Path.Combine(rootDirectory, "bin", configuration, targetFramework);
		var primaryAssembly = Path.Combine(source, "DeepFlowTest.dll");
		if (!File.Exists(primaryAssembly))
			throw new FileNotFoundException($"Compile-time library assembly was not found for target framework '{targetFramework}'.", primaryAssembly);

		var libDirectory = Path.Combine(packageDirectory, "lib", targetFramework);
		Directory.CreateDirectory(libDirectory);
		File.Copy(primaryAssembly, Path.Combine(libDirectory, "DeepFlowTest.dll"), overwrite: true);
		CopyIfExists(Path.Combine(source, "DeepFlowTest.pdb"), Path.Combine(libDirectory, "DeepFlowTest.pdb"));
		CopyIfExists(Path.Combine(source, "DeepFlowTest.xml"), Path.Combine(libDirectory, "DeepFlowTest.xml"));
	}

	private static void WritePackageBuildTargets(string packageDirectory)
	{
		var buildDirectory = Path.Combine(packageDirectory, "build");
		Directory.CreateDirectory(buildDirectory);

		const string targetsBody = """
			<Project>
			  <PropertyGroup>
			    <_DeepFlowTestPackageContentRoot>$(MSBuildThisFileDirectory)..\contentFiles\any\any</_DeepFlowTestPackageContentRoot>
			  </PropertyGroup>
			  <ItemGroup Condition="Exists('$(_DeepFlowTestPackageContentRoot)')">
			    <None Include="$(_DeepFlowTestPackageContentRoot)\payloads\**\*.*">
			      <Link>payloads\%(RecursiveDir)%(Filename)%(Extension)</Link>
			      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
			      <Visible>false</Visible>
			    </None>
			    <None Include="$(_DeepFlowTestPackageContentRoot)\DeepFlowTestResources\**\*.*">
			      <Link>DeepFlowTestResources\%(RecursiveDir)%(Filename)%(Extension)</Link>
			      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
			      <Visible>false</Visible>
			    </None>
			  </ItemGroup>
			</Project>
			""";

		File.WriteAllText(Path.Combine(buildDirectory, "DeepFlowTest.targets"), targetsBody);
	}

	private string ResolvePackageVersion()
	{
		var fromEnvironment = Environment.GetEnvironmentVariable("DEEPFLOWTEST_PACKAGE_VERSION");
		return string.IsNullOrWhiteSpace(fromEnvironment) ? "0.0.0-local" : fromEnvironment;
	}

	private string WritePackageNuspec(string packageDirectory, string packageVersion)
	{
		var nuspecPath = Path.Combine(packageDirectory, "DeepFlowTest.nuspec");
		var dependencyGroups = string.Join(Environment.NewLine, LibraryTargetFrameworks.Select(BuildDependencyGroupXml));

		var nuspec = $"""
			<?xml version="1.0" encoding="utf-8"?>
			<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
			  <metadata>
			    <id>DeepFlowTest</id>
			    <version>{Escape(packageVersion)}</version>
			    <authors>DeepFlowTest</authors>
			    <owners>DeepFlowTest</owners>
			    <requireLicenseAcceptance>false</requireLicenseAcceptance>
			    <description>WPF application automation library.</description>
			    <tags>WPF Automation Testing UI</tags>
			    <projectUrl>https://deepflowtest.local</projectUrl>
			    <repository type="git" url="https://deepflowtest.local/repository" />
			    <dependencies>
			{dependencyGroups}
			    </dependencies>
			    <contentFiles>
			      <files include="any/any/payloads/**/*.*" buildAction="None" copyToOutput="true" flatten="false" />
			      <files include="any/any/DeepFlowTestResources/**/*.*" buildAction="None" copyToOutput="true" flatten="false" />
			    </contentFiles>
			  </metadata>
			  <files>
			    <file src="lib\**\*.*" target="lib" />
			    <file src="contentFiles\**\*.*" target="contentFiles" />
			    <file src="build\**\*.*" target="build" />
			  </files>
			</package>
			""";

		File.WriteAllText(nuspecPath, nuspec);
		return nuspecPath;
	}

	private string BuildDependencyGroupXml(string targetFramework)
	{
		var mappedFramework = MapToNuspecFramework(targetFramework);
		var dependencies = LibraryRuntimeDependencies
			.Select(package => $"        <dependency id=\"{Escape(package)}\" version=\"{Escape(ReadCentralPackageVersion(package))}\" />")
			.ToArray();
		var dependenciesXml = string.Join(Environment.NewLine, dependencies);
		return $"      <group targetFramework=\"{Escape(mappedFramework)}\">{Environment.NewLine}{dependenciesXml}{Environment.NewLine}      </group>";
	}

	private static string MapToNuspecFramework(string targetFramework) => targetFramework switch
	{
		"net461" => ".NETFramework4.6.1",
		"netcoreapp3.1" => ".NETCoreApp3.1",
		"net5.0-windows" => "net5.0-windows7.0",
		_ => targetFramework,
	};

	private static string Escape(string value)
	{
		return value
			.Replace("&", "&amp;", StringComparison.Ordinal)
			.Replace("<", "&lt;", StringComparison.Ordinal)
			.Replace(">", "&gt;", StringComparison.Ordinal)
			.Replace("\"", "&quot;", StringComparison.Ordinal);
	}

	private static IReadOnlyList<string> LibraryTargetFrameworks { get; } = new[]
	{
		"net461",
		"netcoreapp3.1",
		"net5.0-windows",
	};

	private static IReadOnlyList<string> LibraryRuntimeDependencies { get; } = new[]
	{
		"Lib.Harmony",
		"Microsoft.CSharp",
		"Newtonsoft.Json",
		"Serialize.Linq",
		"System.Buffers",
		"System.Memory",
		"System.ValueTuple",
	};

	private static bool IsPackageResourceFile(string path)
	{
		var extension = Path.GetExtension(path);
		return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
			extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
			extension.Equals(".config", StringComparison.OrdinalIgnoreCase) ||
			extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase);
	}

	private static void CopyDirectory(string sourceDirectory, string destinationDirectory, Func<string, bool>? filter = null)
	{
		if (!Directory.Exists(sourceDirectory))
			throw new DirectoryNotFoundException($"Expected package source directory '{sourceDirectory}' was not found.");

		foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			if (filter is not null && !filter(sourceFile))
				continue;

			var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
			var destinationFile = Path.Combine(destinationDirectory, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
			File.Copy(sourceFile, destinationFile, overwrite: true);
		}
	}

	private static void CopyDirectoryIfExists(string sourceDirectory, string destinationDirectory, Func<string, bool>? filter = null)
	{
		if (Directory.Exists(sourceDirectory))
			CopyDirectory(sourceDirectory, destinationDirectory, filter);
	}

	private static void CI()
	{
		Console.WriteLine("CI lane completed declared dependencies.");
	}

	private void RunDotNet(params string[] args)
	{
		RunProcess(dotnet, args);
	}

	private void RunProcess(string fileName, params string[] args)
	{
		var startInfo = new ProcessStartInfo(fileName)
		{
			WorkingDirectory = rootDirectory,
			UseShellExecute = false,
		};

		foreach (var arg in args)
			startInfo.ArgumentList.Add(arg);

		Console.WriteLine($"> {fileName} {string.Join(" ", args)}");
		using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
		process.WaitForExit();
		if (process.ExitCode != 0)
			throw new InvalidOperationException($"{Path.GetFileName(fileName)} exited with code {process.ExitCode}.");
	}

	private string FindMsBuild()
	{
		var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
		var vswhere = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
		if (File.Exists(vswhere))
		{
			var discovered = Capture(vswhere, "-latest", "-products", "*", "-requires", "Microsoft.Component.MSBuild", "-find", @"MSBuild\Current\Bin\amd64\MSBuild.exe");
			var first = discovered.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(first) && File.Exists(first))
				return first;
		}

		var candidates = new[]
		{
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio", "2022", "Professional", "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe"),
			Path.Combine(programFilesX86, "Microsoft Visual Studio", "2019", "Professional", "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe"),
		};

		var match = candidates.FirstOrDefault(File.Exists);
		return match ?? throw new FileNotFoundException("MSBuild.exe was not found. Install the desktop native build tools to compile the native injector.");
	}

	private static string Capture(string fileName, params string[] args)
	{
		var startInfo = new ProcessStartInfo(fileName)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		foreach (var arg in args)
			startInfo.ArgumentList.Add(arg);

		using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
		var output = process.StandardOutput.ReadToEnd();
		process.WaitForExit();
		return output;
	}

	private static BuildOptions Parse(string[] args)
	{
		var configuration = "Debug";
		var targets = new List<string>();

		for (var index = 0; index < args.Length; index++)
		{
			var arg = args[index];
			if (arg.Equals("--configuration", StringComparison.OrdinalIgnoreCase) || arg.Equals("-c", StringComparison.OrdinalIgnoreCase))
			{
				if (index + 1 >= args.Length)
					throw new ArgumentException("Missing configuration value.");

				configuration = args[++index];
			}
			else if (arg.StartsWith("--configuration=", StringComparison.OrdinalIgnoreCase))
			{
				configuration = arg.Substring("--configuration=".Length);
			}
			else if (arg.StartsWith("Configuration=", StringComparison.OrdinalIgnoreCase))
			{
				configuration = arg.Substring("Configuration=".Length);
			}
			else if (!string.IsNullOrWhiteSpace(arg))
			{
				targets.Add(arg);
			}
		}

		if (targets.Count == 0)
			targets.Add("Compile");

		return new BuildOptions(configuration, targets);
	}

	private string ReadCentralPackageVersion(string packageName)
	{
		var propsPath = Path.Combine(rootDirectory, "Directory.Packages.props");
		var marker = $"PackageVersion Include=\"{packageName}\" Version=\"";
		var text = File.ReadAllText(propsPath);
		var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
		if (start < 0)
			throw new InvalidOperationException($"Package '{packageName}' is not declared in Directory.Packages.props.");

		start += marker.Length;
		var end = text.IndexOf('"', start);
		if (end < 0)
			throw new InvalidOperationException($"Package '{packageName}' version declaration is malformed.");

		return text.Substring(start, end - start);
	}

	private string MainSolution => Path.Combine(rootDirectory, "DeepFlowTest.sln");

	private string HarnessSolution => Path.Combine(rootDirectory, "TestHarnesses", "TestHarnesses.sln");

	private string NativeInjectorProject => Path.Combine(rootDirectory, "DeepFlowTest.GenericInjector", "DeepFlowTest.GenericInjector.vcxproj");

	private string CoreTestsProject => Path.Combine(rootDirectory, "DeepFlowTest.Tests", "DeepFlowTest.Tests.csproj");

	private string CliTestsProject => Path.Combine(rootDirectory, "DeepFlowTest.Cli.Tests", "DeepFlowTest.Cli.Tests.csproj");

	private string CliProject => Path.Combine(rootDirectory, "DeepFlowTest.Cli", "DeepFlowTest.Cli.csproj");

	private sealed class BuildTarget
	{
		public BuildTarget(string name, Action action, params string[] dependencies)
		{
			Name = name;
			Action = action;
			Dependencies = dependencies;
		}

		public string Name { get; }

		public Action Action { get; }

		public IReadOnlyList<string> Dependencies { get; }
	}

	private sealed record BuildOptions(string Configuration, IReadOnlyList<string> Targets);

	private sealed record PayloadMapping(string TargetFramework, string PayloadFamily);
}
