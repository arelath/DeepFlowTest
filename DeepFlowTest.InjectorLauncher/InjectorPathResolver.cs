namespace DeepFlowTest.InjectorLauncher;

using System;
using System.IO;

internal static class InjectorPathResolver
{
	public const string ResourceFolderName = "DeepFlowTestResources";
	public const string PayloadAssemblyName = "DeepFlowTest.dll";

	public static InjectorDllPaths GetDllPaths(string architecture, string rootDirectory, string frameworkFamily)
	{
		var normalizedArchitecture = ArchitectureDetector.Normalize(architecture);
		var injectorDllName = normalizedArchitecture switch
		{
			ArchitectureDetector.X86 => "DeepFlowTest.GenericInjector.x86.dll",
			ArchitectureDetector.X64 => "DeepFlowTest.GenericInjector.x64.dll",
			_ => throw new InjectorLauncherException(InjectorExitCode.UnsupportedTarget, $"Unsupported target architecture '{architecture}'."),
		};

		return new InjectorDllPaths(
			injectorDllName,
			ResolveResourcePath(rootDirectory, normalizedArchitecture, injectorDllName),
			string.Empty);
	}

	public static string ResolveResourcePath(string rootDirectory, string architecture, string fileName)
	{
		var normalizedArchitecture = ArchitectureDetector.Normalize(architecture);
		var contentFilePath = Path.Combine(rootDirectory, "contentFiles", "any", "any", ResourceFolderName, normalizedArchitecture, fileName);
		if (File.Exists(contentFilePath))
			return contentFilePath;

		return Path.Combine(rootDirectory, ResourceFolderName, normalizedArchitecture, fileName);
	}

	public static string ResolvePayloadPath(string rootDirectory, string frameworkFamily)
	{
		return ResolvePayloadPath(rootDirectory, frameworkFamily, PayloadAssemblyName);
	}

	public static string ResolvePayloadPath(string rootDirectory, string frameworkFamily, string assemblyNameOrPath)
	{
		if (Path.IsPathRooted(assemblyNameOrPath))
			return assemblyNameOrPath;

		var assemblyFileName = assemblyNameOrPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
			? assemblyNameOrPath
			: $"{assemblyNameOrPath}.dll";

		if (!string.IsNullOrWhiteSpace(frameworkFamily))
		{
			var frameworkPath = Path.Combine(rootDirectory, "payloads", frameworkFamily, assemblyFileName);
			if (File.Exists(frameworkPath))
				return frameworkPath;
		}

		var fallbackPath = Path.Combine(rootDirectory, assemblyFileName);
		if (File.Exists(fallbackPath))
		{
			InjectorLog.Write($"Using development payload fallback '{fallbackPath}'.");
			return fallbackPath;
		}

		throw new FileNotFoundException($"Could not find payload assembly for framework family '{frameworkFamily}'.", fallbackPath);
	}
}

internal sealed class InjectorDllPaths
{
	public InjectorDllPaths(string injectorDllName, string injectorDllPath, string payloadDllPath)
	{
		InjectorDllName = injectorDllName;
		InjectorDllPath = injectorDllPath;
		PayloadDllPath = payloadDllPath;
	}

	public string InjectorDllName { get; }

	public string InjectorDllPath { get; }

	public string PayloadDllPath { get; }
}
