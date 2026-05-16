namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal sealed class ElementMatcherPlanner
{
	public const int ClientSideMatcherMaxNodeCount = 50_000;

	private static readonly IReadOnlyList<string> ClientSideMatcherFallbackPropertyNames =
	[
		"ActualHeight",
		"ActualWidth",
		"AllowDrop",
		"AutomationProperties.Id",
		"AutomationProperties.Name",
		"AutomationProperties.AutomationId",
		"AutomationId",
		"Background",
		"BorderBrush",
		"BorderThickness",
		"BoundarySize",
		"Child",
		"ClassName",
		"Command",
		"Content",
		"CornerRadius",
		"Cursor",
		"DesiredSize",
		"FlowDirection",
		"Focusable",
		"FontFamily",
		"FontSize",
		"FontWeight",
		"Foreground",
		"HasContent",
		"Header",
		"Height",
		"HorizontalAlignment",
		"InputGestureText",
		"IsChecked",
		"IsEnabled",
		"IsExpanded",
		"IsKeyboardFocused",
		"IsMouseCaptured",
		"IsMouseDirectlyOver",
		"IsMouseOver",
		"IsOpen",
		"IsVisible",
		"KeyboardNavigation.ControlTabNavigation",
		"KeyboardNavigation.DirectionalNavigation",
		"KeyboardNavigation.TabNavigation",
		"Language",
		"Left",
		"Margin",
		"MaxHeight",
		"MaxWidth",
		"MinHeight",
		"MinWidth",
		"Name",
		"Opacity",
		"Orientation",
		"Padding",
		"Panel.ZIndex",
		"RenderSize",
		"ScrollViewer.CanContentScroll",
		"ScrollViewer.HorizontalScrollBarVisibility",
		"ScrollViewer.PanningMode",
		"ScrollViewer.VerticalScrollBarVisibility",
		"TabIndex",
		"Text",
		"TextElement.Background",
		"TextElement.FontFamily",
		"TextElement.FontSize",
		"TextElement.FontWeight",
		"TextElement.Foreground",
		"TextTrimming",
		"Title",
		"ToolTip",
		"Top",
		"Uid",
		"VerticalAlignment",
		"Visibility",
		"Width",
		"WindowState",
	];

	private readonly ElementFactory elementFactory;

	public ElementMatcherPlanner(ElementFactory elementFactory)
	{
		this.elementFactory = elementFactory ?? throw new ArgumentNullException(nameof(elementFactory));
	}

	public static TimeSpan TimeoutFromMilliseconds(int timeoutMs) =>
		TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));

	public IReadOnlyList<string> GetPropNamesForMatcher(LambdaExpression matcherExpression, IReadOnlyList<string>? propNames)
	{
		var collectedPropertyNames = ElementPropertyAccessCollector.Collect(matcherExpression);
		return MergePropertyNames(VisualTreePropertyExtractor.DefaultPropertyNames, propNames, collectedPropertyNames)
			?? VisualTreePropertyExtractor.DefaultPropertyNames.ToArray();
	}

	public IReadOnlyList<string> GetPropNamesForRootedMatcher(
		LambdaExpression rootMatcherExpression,
		LambdaExpression matcherExpression,
		IReadOnlyList<string>? propNames)
	{
		var collectedRootPropertyNames = ElementPropertyAccessCollector.Collect(rootMatcherExpression);
		var collectedPropertyNames = ElementPropertyAccessCollector.Collect(matcherExpression);
		return MergePropertyNames(VisualTreePropertyExtractor.DefaultPropertyNames, propNames, collectedRootPropertyNames, collectedPropertyNames)
			?? VisualTreePropertyExtractor.DefaultPropertyNames.ToArray();
	}

	public IReadOnlyList<string> GetClientSideMatcherPropNames(IReadOnlyList<string>? propNames) =>
		MergePropertyNames(VisualTreePropertyExtractor.DefaultPropertyNames, ClientSideMatcherFallbackPropertyNames, propNames)
		?? ClientSideMatcherFallbackPropertyNames.ToArray();

	public static IReadOnlyList<string>? MergePropertyNames(params IEnumerable<string>?[] sources)
	{
		HashSet<string>? merged = null;
		foreach (var source in sources)
		{
			if (source is null)
				continue;

			foreach (var name in source.Where(static item => !string.IsNullOrWhiteSpace(item)))
			{
				merged ??= new HashSet<string>(StringComparer.Ordinal);
				merged.Add(name);
			}
		}

		return merged?.ToArray();
	}

	public ElementRepairInfo CreateElementMatcherRepairInfo(
		Expression<Func<Element, bool?>> matcher,
		Func<Element, bool?> predicate,
		Func<ElementRepairInfo?> repairInfoAccessor)
	{
		var description = ExpressionPayloadSerializer.FormatDiagnosticText(matcher);
		var propertyNames = RequiresClientSideElementPredicate(matcher)
			? GetClientSideMatcherPropNames(propNames: null)
			: ElementPropertyAccessCollector.Collect(matcher).ToArray();
		return new ElementRepairInfo(
			description,
			StableHash(description),
			propertyNames,
			(node, snapshot) => predicate(elementFactory.FromNode(node, snapshot, repairInfoAccessor(), register: false)) == true);
	}

	public ElementRepairInfo CreateTypedElementMatcherRepairInfo<TElement>(
		Expression<Func<TElement, bool?>> matcher,
		Func<TElement, bool?> predicate,
		Func<ElementRepairInfo?> repairInfoAccessor)
		where TElement : Element
	{
		var description = ExpressionPayloadSerializer.FormatDiagnosticText(matcher);
		var propertyNames = RequiresClientSideElementPredicate(matcher)
			? GetClientSideMatcherPropNames(propNames: null)
			: ElementPropertyAccessCollector.Collect(matcher).ToArray();
		return new ElementRepairInfo(
			description,
			StableHash(description),
			propertyNames,
			(node, snapshot) => predicate(elementFactory.Wrap<TElement>(elementFactory.FromNode(node, snapshot, repairInfoAccessor(), register: false))) == true);
	}

	public ElementRepairInfo CreateRootedElementMatcherRepairInfo(
		Expression<Func<Element, bool?>> rootMatcher,
		Func<Element, bool?> rootPredicate,
		Expression<Func<Element, bool?>> matcher,
		Func<Element, bool?> predicate,
		Func<ElementRepairInfo?> repairInfoAccessor,
		IReadOnlyList<string>? propNames)
	{
		var description = $"{ExpressionPayloadSerializer.FormatDiagnosticText(matcher)} under root matcher '{ExpressionPayloadSerializer.FormatDiagnosticText(rootMatcher)}'";
		var propertyNames = GetPropNamesForRootedMatcher(rootMatcher, matcher, propNames);
		return new ElementRepairInfo(
			description,
			StableHash(description),
			propertyNames,
			(node, snapshot) =>
			{
				var element = elementFactory.FromNode(node, snapshot, repairInfoAccessor(), register: false);
				if (predicate(element) != true)
					return false;

				return HasMatchingAncestor(node, snapshot, rootPredicate, repairInfoAccessor);
			});
	}

	public static bool RequiresClientSideElementPredicate(LambdaExpression matcher)
	{
		var detector = new OpaqueDelegateInvocationDetector();
		detector.Visit(matcher);
		return detector.RequiresClientSideEvaluation;
	}

	public static void EnsureServerSideMatcher(LambdaExpression matcher, string argumentName)
	{
		if (RequiresClientSideElementPredicate(matcher))
			throw new InvalidOperationException($"Expression '{argumentName}' uses client-only helpers or delegates and cannot be used in a server-side root-scoped find.");
	}

	private bool HasMatchingAncestor(
		VisualTreeNodeDto node,
		VisualTreeSnapshot snapshot,
		Func<Element, bool?> rootPredicate,
		Func<ElementRepairInfo?> repairInfoAccessor)
	{
		var byId = new Dictionary<string, VisualTreeNodeDto>(StringComparer.Ordinal);
		foreach (var candidate in snapshot.Nodes)
		{
			if (!byId.TryGetValue(candidate.TargetId, out var existing) || existing.ParentId is null)
				byId[candidate.TargetId] = candidate;
		}

		var parentId = node.ParentId;
		while (!string.IsNullOrWhiteSpace(parentId))
		{
			if (!byId.TryGetValue(parentId!, out var parent))
				return false;

			var parentElement = elementFactory.FromNode(parent, snapshot, repairInfoAccessor(), register: false);
			if (rootPredicate(parentElement) == true)
				return true;

			parentId = parent.ParentId;
		}

		return false;
	}

	private static string StableHash(string text)
	{
		using var sha = SHA256.Create();
		var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
		return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
	}

	private sealed class OpaqueDelegateInvocationDetector : ExpressionVisitor
	{
		public bool RequiresClientSideEvaluation { get; private set; }

		protected override Expression VisitInvocation(InvocationExpression node)
		{
			if (IsOpaqueDelegate(node.Expression))
				RequiresClientSideEvaluation = true;

			return base.VisitInvocation(node);
		}

		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			if (node.Method.Name == nameof(Action.Invoke) && IsOpaqueDelegate(node.Object))
				RequiresClientSideEvaluation = true;
			else if (UsesElementValue(node) && IsClientOnlyMethod(node.Method))
				RequiresClientSideEvaluation = true;

			return base.VisitMethodCall(node);
		}

		private static bool IsOpaqueDelegate(Expression? expression) =>
			expression is not null
			&& expression is not LambdaExpression
			&& typeof(Delegate).IsAssignableFrom(expression.Type);

		private static bool UsesElementValue(MethodCallExpression expression) =>
			IsElementType(expression.Object?.Type)
			|| expression.Arguments.Any(static argument => IsElementType(argument.Type));

		private static bool IsElementType(Type? type) =>
			type is not null && typeof(Element).IsAssignableFrom(type);

		private static bool IsClientOnlyMethod(MethodInfo method)
		{
			var declaringType = method.DeclaringType;
			if (declaringType is null)
				return false;

			if (declaringType.Assembly == typeof(AppDriver).Assembly)
				return false;

			var namespaceName = declaringType.Namespace;
			return namespaceName is null || !namespaceName.StartsWith("System", StringComparison.Ordinal);
		}
	}

	private sealed class ElementPropertyAccessCollector : ExpressionVisitor
	{
		private readonly HashSet<string> propertyNames = new(StringComparer.Ordinal);

		public static IReadOnlyCollection<string> Collect(LambdaExpression expression)
		{
			var collector = new ElementPropertyAccessCollector();
			collector.Visit(expression);
			return collector.propertyNames;
		}

		protected override Expression VisitIndex(IndexExpression node)
		{
			if (IsElementExpression(node.Object) && node.Arguments.Count == 1 && TryGetString(node.Arguments[0], out var propertyName))
				propertyNames.Add(propertyName);

			return base.VisitIndex(node);
		}

		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			if (IsElementExpression(node.Object)
				&& node.Arguments.Count == 1
				&& (node.Method.Name == "get_Item" || node.Method.Name == nameof(Element.HasProperty))
				&& TryGetString(node.Arguments[0], out var propertyName))
			{
				propertyNames.Add(propertyName);
			}

			return base.VisitMethodCall(node);
		}

		private static bool IsElementExpression(Expression? expression) =>
			expression is not null && typeof(Element).IsAssignableFrom(expression.Type);

		private static bool TryGetString(Expression expression, out string value)
		{
			while (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert)
				expression = convert.Operand;

			if (expression is ConstantExpression { Value: string constant })
			{
				value = constant;
				return true;
			}

			value = string.Empty;
			return false;
		}
	}
}
