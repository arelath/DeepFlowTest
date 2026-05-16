namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal sealed class ElementRepairService(
	ElementFinder elementFinder,
	VisualTreeClient visualTreeClient,
	ElementFactory elementFactory)
{
	private readonly ElementFinder elementFinder = elementFinder ?? throw new ArgumentNullException(nameof(elementFinder));
	private readonly VisualTreeClient visualTreeClient = visualTreeClient ?? throw new ArgumentNullException(nameof(visualTreeClient));
	private readonly ElementFactory elementFactory = elementFactory ?? throw new ArgumentNullException(nameof(elementFactory));

	public Element Repair(Element element)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));

		if (element.Selector is not null)
		{
			var matches = elementFinder.FindBySelector(element.Selector, maxMatches: 100);
			if (matches.Count == 1)
				return matches[0];

			if (matches.Count > 1 && TryChooseBestRepairMatch(element, matches, out var bestMatch))
				return bestMatch;

			if (matches.Count > 1)
				throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, $"Element '{element.TargetId}' is stale and selector '{element.Selector}' matched multiple replacements.");
		}

		if (element.RepairInfo is { HasMatcher: true } repairInfo)
		{
			var snapshot = visualTreeClient.GetVisualTreeForRepair(repairInfo);
			var matches = snapshot.Nodes
				.Where(node => repairInfo.Matches(node, snapshot))
				.Select(node => elementFactory.FromNode(node, snapshot, repairInfo))
				.ToArray();

			if (matches.Length == 1)
				return matches[0];

			if (matches.Length > 1 && TryChooseBestRepairMatch(element, matches, out var bestMatch))
				return bestMatch;

			if (matches.Length > 1)
				throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, $"Element '{element.TargetId}' is stale and matcher '{repairInfo.Description}' matched multiple replacements.");
		}

		throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"Element '{element.TargetId}' is stale and no replacement matched its selector or matcher.");
	}

	private static bool TryChooseBestRepairMatch(Element staleElement, IReadOnlyCollection<Element> matches, out Element bestMatch)
	{
		var ranked = matches
			.Select(match => (Element: match, Score: ScoreRepairMatch(staleElement, match)))
			.Where(match => match.Score > 0)
			.OrderByDescending(match => match.Score)
			.ToArray();

		if (ranked.Length == 0 || (ranked.Length > 1 && ranked[0].Score == ranked[1].Score))
		{
			bestMatch = null!;
			return false;
		}

		bestMatch = ranked[0].Element;
		return true;
	}

	private static int ScoreRepairMatch(Element staleElement, Element candidate)
	{
		var score = 0;
		if (string.Equals(staleElement.TypeName, candidate.TypeName, StringComparison.Ordinal))
			score += 10;

		if (PropertyEquals(staleElement, candidate, KnownProperties.AutomationId)
			|| PropertyEquals(staleElement, candidate, KnownProperties.AutomationIdAlias))
		{
			score += 100;
		}

		if (PropertyEquals(staleElement, candidate, KnownProperties.AutomationName))
			score += 100;
		if (PropertyEquals(staleElement, candidate, KnownProperties.Name))
			score += 50;
		if (PropertyEquals(staleElement, candidate, KnownProperties.Title))
			score += 5;
		if (PropertyEquals(staleElement, candidate, "ActualWidth"))
			score += 50;
		if (PropertyEquals(staleElement, candidate, "ActualHeight"))
			score += 50;

		if (staleElement.RepairInfo is { HasMatcher: true } repairInfo
			&& repairInfo.Matches(candidate.SnapshotNode, candidate.CurrentSnapshot ?? VisualTreeSnapshot.Create(0, [candidate.SnapshotNode])))
		{
			score += 1;
		}

		return score;
	}

	private static bool PropertyEquals(Element left, Element right, string propertyName)
	{
		if (!left.Properties.TryGetValue(propertyName, out var leftValue) || IsEmpty(leftValue))
			return false;
		if (!right.Properties.TryGetValue(propertyName, out var rightValue) || IsEmpty(rightValue))
			return false;
		return Equals(leftValue, rightValue)
			|| string.Equals(Convert.ToString(leftValue), Convert.ToString(rightValue), StringComparison.Ordinal);
	}

	private static bool IsEmpty(object? value) =>
		value is null || (value is string text && text.Length == 0);
}
