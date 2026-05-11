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
			Directory.CreateDirectory(destination);

			if (!Directory.Exists(source))
				continue;

			foreach (var file in Directory.EnumerateFiles(source, "DeepFlowTest.*", SearchOption.TopDirectoryOnly))
			{
				var destinationFile = Path.Combine(destination, Path.GetFileName(file));
				File.Copy(file, destinationFile, overwrite: true);
			}
		}

		File.WriteAllText(
			Path.Combine(payloadRoot, "REPACKING.md"),
			"Payload repacking is scaffolded. Third-party dependency internalization will be implemented with ILRepack when payload dependencies are finalized." + Environment.NewLine);
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
		var packageDirectory = Path.Combine(rootDirectory, "artifacts", "packages", configuration);
		Directory.CreateDirectory(packageDirectory);
		Console.WriteLine($"Package output directory prepared at {packageDirectory}.");
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
