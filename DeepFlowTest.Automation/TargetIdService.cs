namespace DeepFlowTest.Automation;

using System;
using System.Linq;
using DeepFlowTest.Interop;

public sealed class TargetIdService
{
	public string GetShortId(string targetId)
	{
		if (string.IsNullOrWhiteSpace(targetId))
			return string.Empty;

		var lastDash = targetId.LastIndexOf('-');
		if (lastDash >= 0 && lastDash + 1 < targetId.Length)
			return targetId[(lastDash + 1)..];

		return targetId.Length <= 8 ? targetId : targetId[^8..];
	}

	public string Resolve(string targetIdOrShortId, VisualTreeSnapshot snapshot)
	{
		if (string.IsNullOrWhiteSpace(targetIdOrShortId))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "A target ID is required.");

		if (snapshot.Nodes.Any(node => string.Equals(node.TargetId, targetIdOrShortId, StringComparison.Ordinal)))
			return targetIdOrShortId;

		var matches = snapshot.Nodes
			.Where(node => string.Equals(GetShortId(node.TargetId), targetIdOrShortId, StringComparison.Ordinal)
				|| node.TargetId.EndsWith(targetIdOrShortId, StringComparison.Ordinal))
			.ToArray();

		if (matches.Length == 1)
			return matches[0].TargetId;

		if (matches.Length > 1)
			throw new AutomationException(
				AutomationErrorCodes.AmbiguousTarget,
				$"Short target ID '{targetIdOrShortId}' matches multiple nodes.",
				matches.Select(static node => node.TargetId).ToArray());

		throw new AutomationException(AutomationErrorCodes.StaleTarget, $"Target ID '{targetIdOrShortId}' was not found in the current snapshot.");
	}
}
