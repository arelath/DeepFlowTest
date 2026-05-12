namespace DeepFlowTest.InjectorLauncher;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

internal static class FrameworkDetector
{
	public const string NetFramework = "netframework";
	public const string NetCoreApp = "netcoreapp";
	public const string DotNet = "dotnet";

	public static string Classify(Process process)
	{
		var modules = NativeMethods.GetModules(process)
			.Select(static module => ModuleEvidence.FromModuleEntry(module))
			.ToArray();

		return Classify(modules);
	}

	public static string Classify(IEnumerable<ModuleEvidence> modules)
	{
		var evidence = modules
			.Where(static module => IsRuntimeEvidence(module.ModuleName))
			.Select(static module => new RuntimeEvidence(module, TryGetRealVersion(module), GetEvidencePriority(module.ModuleName)))
			.OrderByDescending(static item => item.Priority)
			.ThenByDescending(static item => item.RealVersion is not null || !string.IsNullOrWhiteSpace(item.Module.ProductVersion))
			.FirstOrDefault();

		if (evidence is null)
			throw new InjectorLauncherException(InjectorExitCode.UnsupportedTarget, "No supported .NET runtime evidence was found in the target process.");

		var version = VersionParser.Parse(evidence.RealVersion?.ProductVersion ?? evidence.Module.ProductVersion ?? string.Empty);
		var major = evidence.RealVersion?.ProductMajorPart ?? version.Major;
		return major switch
		{
			>= 5 => DotNet,
			4 => NetFramework,
			3 when version.Minor >= 1 => NetCoreApp,
			_ => throw new InjectorLauncherException(InjectorExitCode.UnsupportedTarget, $".NET runtime version '{evidence.RealVersion?.ProductVersion ?? evidence.Module.ProductVersion}' is not supported."),
		};
	}

	private static FileVersionInfo? TryGetRealVersion(ModuleEvidence module)
	{
		if (string.IsNullOrWhiteSpace(module.FilePath))
			return null;

		try
		{
			return FileVersionInfo.GetVersionInfo(module.FilePath);
		}
		catch
		{
			// Fake or inaccessible module paths still support tests through ProductVersion.
			return null;
		}
	}

	private static int GetEvidencePriority(string moduleName)
	{
		if (moduleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase))
			return 100;

		if (moduleName.Equals("clr.dll", StringComparison.OrdinalIgnoreCase))
			return 90;

		if (moduleName.Equals("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase))
		{
			return 80;
		}

		if (moduleName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("mscorlib.ni.dll", StringComparison.OrdinalIgnoreCase))
		{
			return 70;
		}

		return 10;
	}

	private static bool IsRuntimeEvidence(string moduleName)
	{
		return moduleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("clr.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("mscorlib.ni.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("PresentationFramework.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.StartsWith("wpfgfx_", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("System.Windows.Forms.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("System.Windows.Forms.Primitives.dll", StringComparison.OrdinalIgnoreCase);
	}
}

internal sealed class RuntimeEvidence
{
	public RuntimeEvidence(ModuleEvidence module, FileVersionInfo? realVersion, int priority)
	{
		Module = module;
		RealVersion = realVersion;
		Priority = priority;
	}

	public ModuleEvidence Module { get; }

	public FileVersionInfo? RealVersion { get; }

	public int Priority { get; }
}

internal sealed class ModuleEvidence
{
	public ModuleEvidence(string moduleName, string filePath = "", string productVersion = "")
	{
		ModuleName = moduleName;
		FilePath = filePath;
		ProductVersion = productVersion;
	}

	public string ModuleName { get; }

	public string FilePath { get; }

	public string ProductVersion { get; }

	public static ModuleEvidence FromModuleEntry(NativeMethods.ModuleEntry module)
	{
		return new ModuleEvidence(module.ModuleName, module.FilePath);
	}
}

internal static class VersionParser
{
	public static Version Parse(string version)
	{
		var versionToParse = version;
		var markerIndex = versionToParse.IndexOfAny(new[] { '-', '+', ' ' });
		if (markerIndex > -1)
			versionToParse = versionToParse.Substring(0, markerIndex);

		return Version.TryParse(versionToParse, out var parsed)
			? parsed
			: new Version();
	}
}
