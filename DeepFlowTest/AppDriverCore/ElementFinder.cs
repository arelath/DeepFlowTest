namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal sealed class ElementFinder(
	DriverCommandClient commandClient,
	VisualTreeClient visualTreeClient,
	ElementFactory elementFactory,
	ElementMatcherPlanner matcherPlanner)
{
	private readonly DriverCommandClient commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
	private readonly VisualTreeClient visualTreeClient = visualTreeClient ?? throw new ArgumentNullException(nameof(visualTreeClient));
	private readonly ElementFactory elementFactory = elementFactory ?? throw new ArgumentNullException(nameof(elementFactory));
	private readonly ElementMatcherPlanner matcherPlanner = matcherPlanner ?? throw new ArgumentNullException(nameof(matcherPlanner));

	public IReadOnlyList<Element> FindBySelector(ElementSelector selector, int maxMatches)
	{
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		return FindRaw(selector, matcherCode: null, matcherHash: null, maxMatches: maxMatches);
	}

	public IReadOnlyList<Element> FindByVisualTreeNodeExpression(
		Expression<Func<VisualTreeNodeDto, bool>> matcher,
		int maxMatches)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		var repairInfo = new ElementRepairInfo(
			ExpressionPayloadSerializer.FormatDiagnosticText(matcher),
			payload.ExpressionHash,
			[],
			(node, _) => predicate(node));
		return FindRaw(null, payload, payload.ExpressionHash, maxMatches, repairInfo);
	}

	public IReadOnlyList<Element> FindByElementExpression(
		Expression<Func<Element, bool?>> matcher,
		Func<Element, bool?> predicate,
		ElementRepairInfo repairInfo,
		int maxMatches,
		IReadOnlyList<string>? propNames = null,
		string? rootTargetId = null,
		bool includeRoot = true,
		int? maxDepth = null,
		int? maxNodeCount = null,
		object? rootMatcherCode = null,
		string? rootMatcherHash = null)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		if (ElementMatcherPlanner.RequiresClientSideElementPredicate(matcher))
			return FindElementsOnClient(predicate, maxMatches, repairInfo, propNames, rootTargetId, includeRoot);

		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		var requestedPropertyNames = matcherPlanner.GetPropNamesForMatcher(matcher, propNames);
		return FindRaw(
			null,
			new Eval(payload.ExpressionJson),
			payload.ExpressionHash,
			maxMatches,
			repairInfo,
			requestedPropertyNames,
			rootTargetId,
			includeRoot,
			maxDepth,
			maxNodeCount,
			rootMatcherCode,
			rootMatcherHash);
	}

	public IReadOnlyList<Element> FindByRootedElementExpression(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		ElementRepairInfo repairInfo,
		int maxMatches,
		IReadOnlyList<string>? propNames = null)
	{
		ElementMatcherPlanner.EnsureServerSideMatcher(rootMatcher, nameof(rootMatcher));
		ElementMatcherPlanner.EnsureServerSideMatcher(matcher, nameof(matcher));
		var rootPayload = ExpressionPayloadSerializer.Serialize(rootMatcher);
		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		var requestedPropertyNames = matcherPlanner.GetPropNamesForRootedMatcher(rootMatcher, matcher, propNames);
		return FindRaw(
			null,
			new Eval(payload.ExpressionJson),
			payload.ExpressionHash,
			maxMatches,
			repairInfo,
			requestedPropertyNames,
			rootTargetId: null,
			includeRoot: false,
			maxDepth: null,
			maxNodeCount: ElementMatcherPlanner.ClientSideMatcherMaxNodeCount,
			rootMatcherCode: new Eval(rootPayload.ExpressionJson),
			rootMatcherHash: rootPayload.ExpressionHash);
	}

	public IReadOnlyList<TElement> FindTypedByElementExpression<TElement>(
		Expression<Func<TElement, bool?>> matcher,
		Func<TElement, bool?> predicate,
		ElementRepairInfo repairInfo,
		int maxMatches)
		where TElement : Element
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		if (ElementMatcherPlanner.RequiresClientSideElementPredicate(matcher))
			return FindTypedElementsOnClient(predicate, maxMatches, repairInfo);

		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		var requestedPropertyNames = matcherPlanner.GetPropNamesForMatcher(matcher, propNames: null);
		return FindRaw(null, new Eval(payload.ExpressionJson), payload.ExpressionHash, maxMatches, repairInfo, requestedPropertyNames)
			.Select(elementFactory.Wrap<TElement>)
			.ToArray();
	}

	public IReadOnlyList<Element> FindRaw(
		ElementSelector? selector,
		object? matcherCode,
		string? matcherHash,
		int maxMatches,
		ElementRepairInfo? repairInfo = null,
		IReadOnlyList<string>? propNames = null,
		string? rootTargetId = null,
		bool includeRoot = true,
		int? maxDepth = null,
		int? maxNodeCount = null,
		object? rootMatcherCode = null,
		string? rootMatcherHash = null)
	{
		var requestedPropertyNames = ElementMatcherPlanner.MergePropertyNames(selector?.RequestedPropertyNames, propNames);
		var response = commandClient.Send<FindElementCommandResponse>(new FindElementCommandRequest
		{
			Selector = selector?.ToDto(),
			RootTargetId = rootTargetId,
			IncludeRoot = includeRoot,
			MaxDepth = maxDepth,
			MaxNodeCount = maxNodeCount,
			PropNames = requestedPropertyNames,
			MatcherCode = matcherCode,
			MatcherHash = matcherHash,
			RootMatcherCode = rootMatcherCode,
			RootMatcherHash = rootMatcherHash,
			MaxMatches = maxMatches,
		});

		return response.Matches
			.Select(match => elementFactory.FromMatch(match, selector, repairInfo))
			.ToArray();
	}

	private IReadOnlyList<Element> FindElementsOnClient(
		Func<Element, bool?> predicate,
		int maxMatches,
		ElementRepairInfo repairInfo,
		IReadOnlyList<string>? propNames,
		string? rootTargetId = null,
		bool includeRoot = true)
	{
		var snapshot = visualTreeClient.GetVisualTree(
			rootTargetId: rootTargetId,
			propNames: matcherPlanner.GetClientSideMatcherPropNames(propNames),
			maxNodeCount: ElementMatcherPlanner.ClientSideMatcherMaxNodeCount);
		var limit = maxMatches <= 0 ? int.MaxValue : maxMatches;
		return snapshot.Nodes
			.Where(node => includeRoot || string.IsNullOrWhiteSpace(rootTargetId) || !string.Equals(node.TargetId, rootTargetId, StringComparison.Ordinal))
			.Select(node => elementFactory.FromNode(node, snapshot, repairInfo))
			.Where(element => predicate(element) == true)
			.Take(limit)
			.ToArray();
	}

	private IReadOnlyList<TElement> FindTypedElementsOnClient<TElement>(
		Func<TElement, bool?> predicate,
		int maxMatches,
		ElementRepairInfo repairInfo)
		where TElement : Element
	{
		var snapshot = visualTreeClient.GetVisualTree(
			rootTargetId: null,
			propNames: matcherPlanner.GetClientSideMatcherPropNames(propNames: null),
			maxNodeCount: ElementMatcherPlanner.ClientSideMatcherMaxNodeCount);
		var limit = maxMatches <= 0 ? int.MaxValue : maxMatches;
		return snapshot.Nodes
			.Select(node => elementFactory.Wrap<TElement>(elementFactory.FromNode(node, snapshot, repairInfo)))
			.Where(element => predicate(element) == true)
			.Take(limit)
			.ToArray();
	}
}
