namespace DeepFlowTest.AppDriverPayload.Patching;

using System;
using System.Collections.Generic;

public abstract class WpfPatcherBase : IWpfPatcher
{
	protected WpfPatcherBase(string frameworkFamily, IEnumerable<OptionalWpfPatch> patches)
	{
		FrameworkFamily = frameworkFamily;
		Patches = patches;
	}

	public string FrameworkFamily { get; }

	protected IEnumerable<OptionalWpfPatch> Patches { get; }

	public WpfPatchResult Apply(Action<string, Exception?>? log = null)
	{
		var result = new WpfPatchResult { FrameworkFamily = FrameworkFamily };
		foreach (var patch in Patches)
		{
			try
			{
				if (!patch.IsAvailable())
				{
					result.AddSkipped(patch.Name);
					log?.Invoke($"Optional WPF patch '{patch.Name}' skipped for {FrameworkFamily}.", null);
					continue;
				}

				patch.Apply();
				result.AddApplied(patch.Name);
				log?.Invoke($"Optional WPF patch '{patch.Name}' applied for {FrameworkFamily}.", null);
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				result.AddFailed(patch.Name, ex);
				log?.Invoke($"Optional WPF patch '{patch.Name}' failed for {FrameworkFamily}; continuing startup.", ex);
			}
		}

		return result;
	}
}
