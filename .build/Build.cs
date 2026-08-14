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
	private readonly bool noTestRecordings;
	private readonly IReadOnlyList<string> requestedTargets;
	private readonly string rootDirectory;
	private readonly Dictionary<string, BuildTarget> targets;
	private readonly HashSet<string> visitedTargets = new(StringComparer.OrdinalIgnoreCase);

	private Build(string[] args)
	{
		var options = Parse(args);
		configuration = options.Configuration;
		noTestRecordings = options.NoTestRecordings;
		requestedTargets = options.Targets;
		dotnet = Environment.GetEnvironmentVariable("DOTNET_EXE") ?? "dotnet";
		rootDirectory = FindRepositoryRoot(AppContext.BaseDirectory);
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
			new BuildTarget("BuildClient", BuildClient, "Restore"),
			new BuildTarget("BuildPayload", BuildPayload, "Restore"),
			new BuildTarget("CompileNativeInjector", CompileNativeInjector),
			new BuildTarget("RepackPayloads", RepackPayloads, "BuildPayload"),
			new BuildTarget("Compile", Compile, "Restore", "CompileNativeInjector"),
			new BuildTarget("TestFast", TestFast, "Compile"),
			new BuildTarget("TestCore", TestCore, "Restore"),
			new BuildTarget("TestClient", TestCore, "BuildClient"),
			new BuildTarget("TestPayload", TestPayload, "BuildPayload"),
			new BuildTarget("TestCli", TestCli, "Restore"),
			new BuildTarget("CompileTestHarnesses", CompileTestHarnesses, "Restore"),
			new BuildTarget("TestIntegration", TestIntegration, "Compile", "CompileTestHarnesses"),
			new BuildTarget("TestCliE2E", TestCliE2E, "Compile", "CompileTestHarnesses"),
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

	private void BuildClient()
	{
		RunDotNet("build", ClientProject, "--configuration", configuration, "--no-restore", "/p:RootBuild=true");
	}

	private void BuildPayload()
	{
		RunDotNet("build", PayloadProject, "--configuration", configuration, "--no-restore", "/p:RootBuild=true");
	}

	private void CompileNativeInjector()
	{
		var msbuild = FindMsBuild();
		RunProcess(msbuild, NativeInjectorProject, "/t:Build", $"/p:Configuration={configuration}", "/p:Platform=Win32", "/m", "/nologo");
		RunProcess(msbuild, NativeInjectorProject, "/t:Build", $"/p:Configuration={configuration}", "/p:Platform=x64", "/m", "/nologo");
	}

	private void RepackPayloads()
	{
		RunProcess(
			FindMsBuild(),
			PayloadProject,
			"/t:RepackPayloads",
			$"/p:Configuration={configuration}",
			"/p:RootBuild=true",
			"/nologo");
	}

	private void TestFast()
	{
		RunDotNetTest(CoreTestsProject, "--configuration", configuration, "--no-restore");
		RunDotNetTest(PayloadTestsProject, "--configuration", configuration, "--no-restore");
		RunDotNetTest(CliTestsProject, "--configuration", configuration, "--no-restore");
	}

	private void TestCore()
	{
		RunDotNetTest(CoreTestsProject, "--configuration", configuration, "--no-restore");
	}

	private void TestPayload()
	{
		RunDotNetTest(PayloadTestsProject, "--configuration", configuration, "--no-restore");
	}

	private void TestCli()
	{
		RunDotNetTest(CliTestsProject, "--configuration", configuration, "--no-restore");
	}

	private void CompileTestHarnesses()
	{
		RunDotNet("build", HarnessSolution, "--configuration", configuration, "--no-restore");
	}

	private void TestIntegration()
	{
		RunDotNetTest(
			CoreTestsProject,
			"--configuration",
			configuration,
			"--no-build",
			"--filter",
			"FullyQualifiedName~RunningProcessAttachIntegrationTests");
	}

	private void TestCliE2E()
	{
		RunProcess(
			"pwsh",
			"-NoLogo",
			"-NoProfile",
			"-NonInteractive",
			"-File",
			Path.Combine(rootDirectory, "Tools", "Run-CliE2ESuite.ps1"),
			"-Configuration",
			configuration,
			"-SkipBuild");
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
		RunDotNet("pack", ClientProject, "--configuration", configuration, "--no-build", "--no-restore");
		RunDotNet("pack", MediaPackageProject, "--configuration", configuration, "--no-build", "--no-restore");
	}

	private void Publish()
	{
		Console.WriteLine("Publish lane produced CLI artifacts and NuGet package.");
	}

	private static void CI()
	{
		Console.WriteLine("CI lane completed declared dependencies.");
	}

	private void RunDotNet(params string[] args)
	{
		RunProcess(dotnet, args);
	}

	private void RunDotNetTest(string project, params string[] args)
	{
		var fullArgs = new List<string> { "test", project };
		fullArgs.AddRange(args);
		if (noTestRecordings)
		{
			fullArgs.Add("--");
			fullArgs.Add("TestRunParameters.Parameter(name=\"DeepFlowTestTestRecordings\",value=\"off\")");
		}

		RunDotNet(fullArgs.ToArray());
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

	private static string FindRepositoryRoot(string startDirectory)
	{
		var directory = new DirectoryInfo(startDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeepFlowTest.sln")))
			directory = directory.Parent;

		return directory?.FullName ?? throw new DirectoryNotFoundException("DeepFlowTest repository root was not found.");
	}

	private static BuildOptions Parse(string[] args)
	{
		var configuration = "Debug";
		var noTestRecordings = false;
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
			else if (arg.Equals("--no-test-recordings", StringComparison.OrdinalIgnoreCase)
				|| arg.Equals("NoTestRecordings=true", StringComparison.OrdinalIgnoreCase))
			{
				noTestRecordings = true;
			}
			else if (!string.IsNullOrWhiteSpace(arg))
			{
				targets.Add(arg);
			}
		}

		if (targets.Count == 0)
			targets.Add("Compile");

		return new BuildOptions(configuration, noTestRecordings, targets);
	}

	private string MainSolution => Path.Combine(rootDirectory, "DeepFlowTest.sln");

	private string HarnessSolution => Path.Combine(rootDirectory, "TestHarnesses", "TestHarnesses.sln");

	private string NativeInjectorProject => Path.Combine(rootDirectory, "DeepFlowTest.GenericInjector", "DeepFlowTest.GenericInjector.vcxproj");

	private string CoreTestsProject => Path.Combine(rootDirectory, "DeepFlowTest.Tests", "DeepFlowTest.Tests.csproj");

	private string ClientProject => Path.Combine(rootDirectory, "DeepFlowTest", "DeepFlowTest.csproj");

	private string PayloadProject => Path.Combine(rootDirectory, "DeepFlowTest.Payload", "DeepFlowTest.Payload.csproj");

	private string MediaPackageProject => Path.Combine(rootDirectory, "DeepFlowTest.Media.FFmpeg", "DeepFlowTest.Media.FFmpeg.csproj");

	private string PayloadTestsProject => Path.Combine(rootDirectory, "DeepFlowTest.Payload.Tests", "DeepFlowTest.Payload.Tests.csproj");

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

	private sealed record BuildOptions(string Configuration, bool NoTestRecordings, IReadOnlyList<string> Targets);

}
