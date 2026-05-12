namespace DeepFlowTest.AppDriverPayload.Patching;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

public sealed class RuntimeWpfPatchCoordinator
{
	private readonly IRuntimeFrameworkDetector frameworkDetector;
	private readonly Dictionary<string, IWpfPatcher> patchers;

	public RuntimeWpfPatchCoordinator(IRuntimeFrameworkDetector? frameworkDetector = null, IEnumerable<IWpfPatcher>? patchers = null)
	{
		this.frameworkDetector = frameworkDetector ?? new RuntimeFrameworkDetector();
		this.patchers = (patchers ?? CreateDefaultPatchers()).ToDictionary(static patcher => patcher.FrameworkFamily, StringComparer.Ordinal);
	}

	public static RuntimeWpfPatchCoordinator Default { get; } = new();

	public WpfPatchResult ApplyCurrentRuntime(Action<string, Exception?>? log = null)
	{
		var family = frameworkDetector.GetFrameworkFamily();
		if (!patchers.TryGetValue(family, out var patcher))
		{
			var result = new WpfPatchResult { FrameworkFamily = family };
			log?.Invoke($"No WPF patcher is registered for runtime family '{family}'.", null);
			return result;
		}

		return patcher.Apply(log);
	}

	public IWpfPatcher? SelectPatcher() =>
		patchers.TryGetValue(frameworkDetector.GetFrameworkFamily(), out var patcher) ? patcher : null;

	private static IEnumerable<IWpfPatcher> CreateDefaultPatchers()
	{
		yield return new NetFrameworkWpfPatcher();
		yield return new NetCoreWpfPatcher();
		yield return new ModernNetWpfPatcher();
	}
}

public interface IRuntimeFrameworkDetector
{
	string GetFrameworkFamily();
}

public sealed class RuntimeFrameworkDetector : IRuntimeFrameworkDetector
{
	public string GetFrameworkFamily()
	{
		var description = RuntimeInformation.FrameworkDescription ?? string.Empty;
		if (description.IndexOf(".NET Framework", StringComparison.OrdinalIgnoreCase) >= 0)
			return RuntimeFrameworkFamilies.NetFramework;

		if (description.IndexOf(".NET Core", StringComparison.OrdinalIgnoreCase) >= 0)
			return RuntimeFrameworkFamilies.NetCore;

		if (description.IndexOf(".NET", StringComparison.OrdinalIgnoreCase) >= 0)
			return RuntimeFrameworkFamilies.ModernNet;

		return RuntimeFrameworkFamilies.Unknown;
	}
}
