namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility.WpfUtility.Tree;
using Serialize.Linq;
using Serialize.Linq.Factories;
using Serialize.Linq.Serializers;
using SerializeJsonSerializer = Serialize.Linq.Serializers.JsonSerializer;

internal static class FindElementCommand
{
	private static readonly FactorySettings ExpressionFactorySettings = new() { AllowPrivateFieldAccess = true };

	public static object Process(FindElementCommandRequest request, TreeService treeService, ExpressionCache expressionCache)
	{
		_ = request ?? throw new ArgumentNullException(nameof(request));
		_ = treeService ?? throw new ArgumentNullException(nameof(treeService));
		_ = expressionCache ?? throw new ArgumentNullException(nameof(expressionCache));

		var propertyNames = GetRequestedPropertyNames(request).ToArray();
		var snapshot = treeService.CaptureSnapshot(new TreeSnapshotOptions
		{
			RequestedPropertyNames = propertyNames,
			IncludeHidden = true,
			MaxNodeCount = 1000,
		});

		var expressionMatcher = TryCreateExpressionMatcher(request, expressionCache);
		var maxMatches = request.MaxMatches <= 0 ? int.MaxValue : request.MaxMatches;
		var matches = snapshot.Nodes
			.Where(node => MatchesSelector(node, request.Selector))
			.Where(node => expressionMatcher is null || expressionMatcher(node))
			.Take(maxMatches)
			.Select(static node => new FindElementMatchResponse
			{
				TargetId = node.TargetId,
				TypeName = node.TypeName,
				FrameworkTypeName = node.FrameworkTypeName,
				Properties = node.Properties,
			})
			.ToList();

		return new FindElementCommandResponse
		{
			Status = matches.Count == 0 ? ProtocolConstants.Statuses.NoMatch : ProtocolConstants.Statuses.Ok,
			Matches = matches,
			MatchCount = matches.Count,
			MaxMatches = request.MaxMatches,
		};
	}

	private static IEnumerable<string> GetRequestedPropertyNames(FindElementCommandRequest request)
	{
		var names = new HashSet<string>(request.PropNames ?? VisualTreePropertyExtractor.DefaultPropertyNames, StringComparer.Ordinal);
		if (request.Selector is not null)
		{
			if (!string.IsNullOrWhiteSpace(request.Selector.Name))
				names.Add("Name");
			if (!string.IsNullOrWhiteSpace(request.Selector.AutomationId))
				names.Add("AutomationProperties.AutomationId");
			if (!string.IsNullOrWhiteSpace(request.Selector.Text))
				names.Add("Text");
			if (!string.IsNullOrWhiteSpace(request.Selector.Content))
				names.Add("Content");

			foreach (var propertyName in request.Selector.Properties.Keys)
				names.Add(propertyName);
		}

		return names;
	}

	private static bool MatchesSelector(VisualTreeNodeDto node, ElementSelectorDto? selector)
	{
		if (selector is null)
			return true;

		var typeName = selector.TypeName;
		if (!string.IsNullOrWhiteSpace(typeName) && !MatchesType(node, typeName!))
			return false;

		if (!string.IsNullOrWhiteSpace(selector.Name) && !PropertyEquals(node, "Name", selector.Name))
			return false;

		if (!string.IsNullOrWhiteSpace(selector.AutomationId)
			&& !PropertyEquals(node, "AutomationProperties.AutomationId", selector.AutomationId)
			&& !PropertyEquals(node, "AutomationId", selector.AutomationId))
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(selector.Text) && !PropertyEquals(node, "Text", selector.Text))
			return false;

		if (!string.IsNullOrWhiteSpace(selector.Content) && !PropertyEquals(node, "Content", selector.Content))
			return false;

		foreach (var property in selector.Properties)
			if (!PropertyEquals(node, property.Key, property.Value))
				return false;

		return true;
	}

	private static bool MatchesType(VisualTreeNodeDto node, string typeName)
	{
		return string.Equals(node.TypeName, typeName, StringComparison.Ordinal)
			|| string.Equals(node.FrameworkTypeName, typeName, StringComparison.Ordinal)
			|| (node.FrameworkTypeName?.EndsWith("." + typeName, StringComparison.Ordinal) == true);
	}

	private static bool PropertyEquals(VisualTreeNodeDto node, string propertyName, object? expected)
	{
		if (!node.Properties.TryGetValue(propertyName, out var actual))
			return false;

		if (actual is PropertyExtractionError)
			return false;

		if (actual is null || expected is null)
			return actual is null && expected is null;

		if (actual is string actualString)
			return string.Equals(actualString, Convert.ToString(expected, System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

		return actual.Equals(expected);
	}

	private static Func<VisualTreeNodeDto, bool>? TryCreateExpressionMatcher(FindElementCommandRequest request, ExpressionCache expressionCache)
	{
		if (request.MatcherCode is null)
			return null;

		var payload = MessagePacker.ConvertTo<ExpressionMatcherPayload>(request.MatcherCode);
		var matcherHash = request.MatcherHash;
		if (string.IsNullOrWhiteSpace(payload.ExpressionHash) && !string.IsNullOrWhiteSpace(matcherHash))
			payload.ExpressionHash = matcherHash!;

		return expressionCache.GetOrCompile(payload, CompileExpressionMatcher);
	}

	private static Func<VisualTreeNodeDto, bool> CompileExpressionMatcher(ExpressionMatcherPayload payload)
	{
		if (string.IsNullOrWhiteSpace(payload.ExpressionJson))
			throw new InvalidOperationException("Expression matcher payload is empty.");

		var serializer = new ExpressionSerializer(new SerializeJsonSerializer(), ExpressionFactorySettings);
		serializer.AddKnownType(typeof(VisualTreeNodeDto));
		var expression = serializer.DeserializeText(payload.ExpressionJson, new ExpressionContext { AllowPrivateFieldAccess = true });
		if (expression is Expression<Func<VisualTreeNodeDto, bool>> typedExpression)
			return typedExpression.Compile();

		if (expression is LambdaExpression lambdaExpression && lambdaExpression.Compile() is Func<VisualTreeNodeDto, bool> typedDelegate)
			return typedDelegate;

		throw new InvalidOperationException("Expression matcher must compile to Func<VisualTreeNodeDto, bool>.");
	}
}
