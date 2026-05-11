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
		FileVersionInfo? realVersion = null;
		ModuleEvidence? evidence = null;
		foreach (var module in modules)
		{
			if (!IsRuntimeEvidence(module.ModuleName))
				continue;

			evidence = module;
			if (!string.IsNullOrWhiteSpace(module.FilePath))
			{
				try
				{
					realVersion = FileVersionInfo.GetVersionInfo(module.FilePath);
				}
				catch
				{
					// Fake or inaccessible module paths still support tests through ProductVersion.
				}
			}

			if (realVersion is not null || !string.IsNullOrWhiteSpace(module.ProductVersion))
				break;
		}

		if (evidence is null)
			return NetFramework;

		var version = VersionParser.Parse(realVersion?.ProductVersion ?? evidence.ProductVersion ?? string.Empty);
		var major = realVersion?.ProductMajorPart ?? version.Major;
		return major switch
		{
			>= 5 => DotNet,
			4 => NetFramework,
			3 when version.Minor >= 1 => NetCoreApp,
			_ => throw new InjectorLauncherException(InjectorExitCode.UnsupportedTarget, $".NET runtime version '{realVersion?.ProductVersion ?? evidence.ProductVersion}' is not supported."),
		};
	}

	private static bool IsRuntimeEvidence(string moduleName)
	{
		return moduleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("PresentationFramework.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.StartsWith("wpfgfx_", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("System.Windows.Forms.dll", StringComparison.OrdinalIgnoreCase) ||
			moduleName.Equals("System.Windows.Forms.Primitives.dll", StringComparison.OrdinalIgnoreCase);
	}
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
