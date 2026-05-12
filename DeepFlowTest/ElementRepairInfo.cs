namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using DeepFlowTest.Interop;

internal sealed class ElementRepairInfo
{
	private readonly Func<VisualTreeNodeDto, VisualTreeSnapshot, bool>? matcher;

	public ElementRepairInfo(
		string description,
		string? matcherHash,
		IReadOnlyCollection<string>? requestedPropertyNames,
		Func<VisualTreeNodeDto, VisualTreeSnapshot, bool>? matcher)
	{
		Description = description;
		MatcherHash = matcherHash;
		RequestedPropertyNames = requestedPropertyNames ?? [];
		this.matcher = matcher;
	}

	public string Description { get; }

	public string? MatcherHash { get; }

	public IReadOnlyCollection<string> RequestedPropertyNames { get; }

	public bool HasMatcher => matcher is not null;

	public bool Matches(VisualTreeNodeDto node, VisualTreeSnapshot snapshot) =>
		matcher?.Invoke(node, snapshot) == true;
}
