namespace DeepFlowTest.InjectorLauncher;

using System;
using System.IO;

internal static class InjectorPathResolver
{
	public const string ResourceFolderName = "DeepFlowTestResources";
	public const string PayloadAssemblyName = "DeepFlowTest.dll";
	private static string? rootDirectoryOverride;

	public static string RootDirectory => rootDirectoryOverride ?? AppContext.BaseDirectory;

	public static IDisposable OverrideRootDirectoryForTests(string rootDirectory)
	{
		var previous = rootDirectoryOverride;
		rootDirectoryOverride = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
		return new RestoreRootDirectory(previous);
	}

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
		var normalizedRootDirectory = Path.GetFullPath(rootDirectory);

		// Common deployment: launcher exe runs from <bin>/DeepFlowTestResources/<arch>/, with its
		// architecture-specific DLLs sitting beside it. Prefer that sibling layout.
		var siblingPath = Path.Combine(normalizedRootDirectory, fileName);
		if (File.Exists(siblingPath))
			return siblingPath;

		var contentFilePath = Path.Combine(normalizedRootDirectory, "contentFiles", "any", "any", ResourceFolderName, normalizedArchitecture, fileName);
		if (File.Exists(contentFilePath))
			return contentFilePath;

		return Path.Combine(ResolveResourceDirectory(normalizedRootDirectory, normalizedArchitecture), fileName);
	}

	private static string ResolveResourceDirectory(string rootDirectory, string architecture)
	{
		var trimmedRoot = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var directoryName = Path.GetFileName(trimmedRoot);
		if (directoryName.Equals(architecture, StringComparison.OrdinalIgnoreCase))
		{
			var parent = Path.GetDirectoryName(trimmedRoot);
			if (parent is not null && Path.GetFileName(parent).Equals(ResourceFolderName, StringComparison.OrdinalIgnoreCase))
				return trimmedRoot;
		}

		if (directoryName.Equals(ResourceFolderName, StringComparison.OrdinalIgnoreCase))
			return Path.Combine(trimmedRoot, architecture);

		return Path.Combine(trimmedRoot, ResourceFolderName, architecture);
	}

	public static string ResolvePayloadPath(string rootDirectory, string frameworkFamily)
	{
		return ResolvePayloadPath(rootDirectory, frameworkFamily, PayloadAssemblyName);
	}

	public static string ResolvePayloadPath(string rootDirectory, string frameworkFamily, string assemblyNameOrPath)
	{
		if (Path.IsPathRooted(assemblyNameOrPath))
			return assemblyNameOrPath;

		ValidateFrameworkFamily(frameworkFamily);

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

	private static void ValidateFrameworkFamily(string frameworkFamily)
	{
		if (string.IsNullOrWhiteSpace(frameworkFamily))
			return;

		if (frameworkFamily is FrameworkDetector.NetFramework or FrameworkDetector.NetCoreApp or FrameworkDetector.DotNet)
			return;

		throw new InjectorLauncherException(InjectorExitCode.UnsupportedTarget, $"Unsupported target framework family '{frameworkFamily}'.");
	}

	private sealed class RestoreRootDirectory : IDisposable
	{
		private readonly string? previous;

		public RestoreRootDirectory(string? previous)
		{
			this.previous = previous;
		}

		public void Dispose()
		{
			rootDirectoryOverride = previous;
		}
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
