namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DeepFlowTest.Interop;

internal sealed class ElementQueryService(
	ElementFinder elementFinder,
	ElementMatcherPlanner matcherPlanner,
	ElementWaiter waiter,
	VisualTreeClient visualTreeClient,
	ElementFactory elementFactory)
{
	private const int NoMatchDiagnosticMaxNodeCount = 200;
	private const int NoMatchDiagnosticMaxElements = 25;

	private readonly ElementFinder elementFinder = elementFinder ?? throw new ArgumentNullException(nameof(elementFinder));
	private readonly ElementMatcherPlanner matcherPlanner = matcherPlanner ?? throw new ArgumentNullException(nameof(matcherPlanner));
	private readonly ElementWaiter waiter = waiter ?? throw new ArgumentNullException(nameof(waiter));
	private readonly VisualTreeClient visualTreeClient = visualTreeClient ?? throw new ArgumentNullException(nameof(visualTreeClient));
	private readonly ElementFactory elementFactory = elementFactory ?? throw new ArgumentNullException(nameof(elementFactory));

	public Element GetElement(ElementSelector selector)
	{
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		return waiter.PollForElement(
			() => elementFinder.FindBySelector(selector, maxMatches: 2),
			selector.ToString());
	}

	public Element GetElement(Expression<Func<VisualTreeNodeDto, bool>> matcher) =>
		GetElements(matcher, maxMatches: 1).SingleOrDefault()
		?? throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched expression '{ExpressionPayloadSerializer.FormatDiagnosticText(matcher)}'.");

	public Element GetElement(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = matcherPlanner.CreateElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		return waiter.PollForElement(
			() => elementFinder.FindByElementExpression(matcher, predicate, repairInfo, maxMatches: 2, propNames: propNames),
			ExpressionPayloadSerializer.FormatDiagnosticText(matcher),
			ElementMatcherPlanner.TimeoutFromMilliseconds(timeoutMs));
	}

	public TElement GetElement<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
		where TElement : Element =>
		elementFactory.Wrap<TElement>(GetElement(matcher, timeoutMs, propNames));

	public Element GetElement(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		int timeoutMs,
		IReadOnlyList<string>? propNames)
	{
		_ = rootMatcher ?? throw new ArgumentNullException(nameof(rootMatcher));
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		ElementMatcherPlanner.EnsureServerSideMatcher(rootMatcher, nameof(rootMatcher));
		ElementMatcherPlanner.EnsureServerSideMatcher(matcher, nameof(matcher));
		var rootPredicate = rootMatcher.Compile();
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = matcherPlanner.CreateRootedElementMatcherRepairInfo(rootMatcher, rootPredicate, matcher, predicate, () => repairInfo, propNames);
		return waiter.PollForElement(
			() => elementFinder.FindByRootedElementExpression(rootMatcher, matcher, repairInfo, maxMatches: 2, propNames: propNames),
			$"{ExpressionPayloadSerializer.FormatDiagnosticText(matcher)} under root matcher '{ExpressionPayloadSerializer.FormatDiagnosticText(rootMatcher)}'",
			ElementMatcherPlanner.TimeoutFromMilliseconds(timeoutMs));
	}

	public Element GetElement(Element root, Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
	{
		_ = root ?? throw new ArgumentNullException(nameof(root));
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = matcherPlanner.CreateElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		var diagnosticPropertyNames = matcherPlanner.GetPropNamesForMatcher(matcher, propNames);
		return waiter.PollForElement(
			() => elementFinder.FindByElementExpression(
				matcher,
				predicate,
				repairInfo,
				maxMatches: 2,
				propNames: propNames,
				rootTargetId: root.TargetId,
				includeRoot: false,
				maxNodeCount: ElementMatcherPlanner.ClientSideMatcherMaxNodeCount),
			$"{ExpressionPayloadSerializer.FormatDiagnosticText(matcher)} under '{root.TargetId}'",
			ElementMatcherPlanner.TimeoutFromMilliseconds(timeoutMs),
			() => BuildRootNoMatchDiagnostic(root.TargetId, diagnosticPropertyNames));
	}

	public IReadOnlyList<Element> GetElements(ElementSelector selector, int maxMatches)
	{
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		return elementFinder.FindBySelector(selector, maxMatches);
	}

	public IReadOnlyList<Element> GetElements(Expression<Func<VisualTreeNodeDto, bool>> matcher, int maxMatches)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		return elementFinder.FindByVisualTreeNodeExpression(matcher, maxMatches);
	}

	public IReadOnlyList<Element> GetElements(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = matcherPlanner.CreateElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		return waiter.PollForAny(
			() => elementFinder.FindByElementExpression(matcher, predicate, repairInfo, maxMatches: 0, propNames: propNames),
			ElementMatcherPlanner.TimeoutFromMilliseconds(timeoutMs));
	}

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
		where TElement : Element =>
		GetElements(matcher, timeoutMs, propNames)
			.Select(elementFactory.Wrap<TElement>)
			.ToArray();

	public IReadOnlyList<Element> GetElements(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		int timeoutMs,
		IReadOnlyList<string>? propNames)
	{
		_ = rootMatcher ?? throw new ArgumentNullException(nameof(rootMatcher));
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		ElementMatcherPlanner.EnsureServerSideMatcher(rootMatcher, nameof(rootMatcher));
		ElementMatcherPlanner.EnsureServerSideMatcher(matcher, nameof(matcher));
		var rootPredicate = rootMatcher.Compile();
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = matcherPlanner.CreateRootedElementMatcherRepairInfo(rootMatcher, rootPredicate, matcher, predicate, () => repairInfo, propNames);
		return waiter.PollForAny(
			() => elementFinder.FindByRootedElementExpression(rootMatcher, matcher, repairInfo, maxMatches: 0, propNames: propNames),
			ElementMatcherPlanner.TimeoutFromMilliseconds(timeoutMs));
	}

	public IReadOnlyList<Element> GetElements(Element root, Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
	{
		_ = root ?? throw new ArgumentNullException(nameof(root));
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = matcherPlanner.CreateElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		return waiter.PollForAny(
			() => elementFinder.FindByElementExpression(
				matcher,
				predicate,
				repairInfo,
				maxMatches: 0,
				propNames: propNames,
				rootTargetId: root.TargetId,
				includeRoot: false,
				maxNodeCount: ElementMatcherPlanner.ClientSideMatcherMaxNodeCount),
			ElementMatcherPlanner.TimeoutFromMilliseconds(timeoutMs));
	}

	public TElement GetElement<TElement>(Expression<Func<TElement, bool?>> matcher)
		where TElement : Element =>
		GetElements(matcher, maxMatches: 1).SingleOrDefault()
		?? throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched expression '{ExpressionPayloadSerializer.FormatDiagnosticText(matcher)}'.");

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<TElement, bool?>> matcher, int maxMatches)
		where TElement : Element
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = matcherPlanner.CreateTypedElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		return elementFinder.FindTypedByElementExpression(matcher, predicate, repairInfo, maxMatches);
	}

	private string BuildRootNoMatchDiagnostic(string rootTargetId, IReadOnlyList<string> propNames)
	{
		var snapshot = visualTreeClient.GetVisualTree(rootTargetId, propNames, maxNodeCount: NoMatchDiagnosticMaxNodeCount);
		var elements = snapshot.Nodes
			.Take(NoMatchDiagnosticMaxElements)
			.Select(node => elementFactory.FromNode(node, snapshot, register: false))
			.ToArray();
		return ElementDiagnosticFormatter.BuildRootNoMatchDiagnostic(rootTargetId, snapshot, elements, NoMatchDiagnosticMaxElements);
	}
}
