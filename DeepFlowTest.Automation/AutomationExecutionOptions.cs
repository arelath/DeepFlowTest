namespace DeepFlowTest.Automation;

using System;
using System.Collections.Generic;
using DeepFlowTest.Contracts;

public enum ObservationMode
{
	None,
	Target,
	Tree,
}

public sealed record AutomationExecutionOptions(
	int TimeoutMs,
	int TreeLimit,
	IReadOnlyList<string> Properties,
	ObservationMode After,
	bool UseShortIds)
{
	public TreeShape TreeShape { get; init; } = TreeShape.Flat;

	public AutomationExecutionOptions Validate()
	{
		if (TimeoutMs <= 0)
			throw new ArgumentOutOfRangeException(nameof(TimeoutMs));
		if (TreeLimit <= 0)
			throw new ArgumentOutOfRangeException(nameof(TreeLimit));
		ArgumentNullException.ThrowIfNull(Properties);
		return this;
	}
}
