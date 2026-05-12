namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;

public static class AppDriverProcessResolver
{
	public static ITargetProcess ResolveByName(IEnumerable<ITargetProcess> processes, string processName, bool allowContainsMatch = true)
	{
		_ = processes ?? throw new ArgumentNullException(nameof(processes));
		if (string.IsNullOrWhiteSpace(processName))
			throw new ArgumentException("Process name is required.", nameof(processName));

		var candidates = processes.Where(static process => !process.HasExited).ToArray();
		var exact = candidates
			.Where(process => string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		if (exact.Length == 1)
			return exact[0];
		if (exact.Length > 1)
			throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, $"Multiple processes are named '{processName}'.");

		if (!allowContainsMatch)
			throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No process named '{processName}' was found.");

		var contains = candidates
			.Where(process => process.ProcessName.IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0)
			.ToArray();
		if (contains.Length == 1)
			return contains[0];
		if (contains.Length > 1)
			throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, $"Multiple processes contain '{processName}' in their names.");

		throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No process matching '{processName}' was found.");
	}
}
