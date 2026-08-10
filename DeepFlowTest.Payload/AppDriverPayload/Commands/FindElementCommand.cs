namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility.WpfUtility.Tree;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class FindElementCommand
{
	private const int DefaultMaxNodeCount = 50_000;

	private delegate bool SnapshotNodeMatcher(VisualTreeNodeDto node, VisualTreeSnapshot snapshot);

	public static object Process(FindElementCommandRequest request, TreeService treeService, ExpressionCache expressionCache)
	{
		_ = request ?? throw new ArgumentNullException(nameof(request));
		_ = treeService ?? throw new ArgumentNullException(nameof(treeService));
		_ = expressionCache ?? throw new ArgumentNullException(nameof(expressionCache));

		var propertyNames = GetRequestedPropertyNames(request).ToArray();
		var snapshot = treeService.CaptureSnapshot(new TreeSnapshotOptions
		{
			RootTargetId = request.RootTargetId,
			RequestedPropertyNames = propertyNames,
			IncludeHidden = true,
			MaxDepth = request.MaxDepth,
			MaxNodeCount = request.MaxNodeCount ?? DefaultMaxNodeCount,
		});
		LogIfTruncated(snapshot, request.MaxNodeCount ?? DefaultMaxNodeCount, "initial find snapshot");

		var expressionMatcher = TryCreateExpressionMatcher(request.MatcherCode, request.MatcherHash, expressionCache);
		var rootMatcher = TryCreateExpressionMatcher(request.RootMatcherCode, request.RootMatcherHash, expressionCache);
		var rootIds = rootMatcher is null
			? null
			: new HashSet<string>(
				snapshot.Nodes
					.Where(node => rootMatcher(node, snapshot))
					.Select(static node => node.TargetId),
				StringComparer.Ordinal);
		var parentIdsByTargetId = rootIds is null
			? null
			: BuildParentIdLookup(snapshot.Nodes);
		var maxMatches = request.MaxMatches <= 0 ? int.MaxValue : request.MaxMatches;
		if (rootMatcher is not null && string.IsNullOrWhiteSpace(request.RootTargetId))
		{
			var scopedMatches = FindMatchesInMatchedRootSnapshots(
				request,
				treeService,
				propertyNames,
				snapshot,
				expressionMatcher,
				rootIds!,
				parentIdsByTargetId,
				maxMatches);
			return CreateResponse(scopedMatches, request.MaxMatches);
		}

		var matches = snapshot.Nodes
			.Where(node => IsInRequestedRootScope(node, request, rootIds, parentIdsByTargetId))
			.Where(node => MatchesSelector(node, request.Selector))
			.Where(node => expressionMatcher is null || expressionMatcher(node, snapshot))
			.Take(maxMatches)
			.Select(node => ToMatch(node, snapshot))
			.ToList();

		return CreateResponse(matches, request.MaxMatches);
	}

	private static List<FindElementMatchResponse> FindMatchesInMatchedRootSnapshots(
		FindElementCommandRequest request,
		TreeService treeService,
		IReadOnlyList<string> propertyNames,
		VisualTreeSnapshot initialSnapshot,
		SnapshotNodeMatcher? expressionMatcher,
		IEnumerable<string> rootIds,
		IReadOnlyDictionary<string, string?>? parentIdsByTargetId,
		int maxMatches)
	{
		List<FindElementMatchResponse> matches = [];
		var seenTargetIds = new HashSet<string>(StringComparer.Ordinal);
		var rootIdSet = rootIds as ISet<string> ?? new HashSet<string>(rootIds, StringComparer.Ordinal);
		foreach (var node in initialSnapshot.Nodes)
		{
			if (matches.Count >= maxMatches)
				break;
			if (!IsInRequestedRootScope(node, request, rootIdSet, parentIdsByTargetId))
				continue;
			if (!seenTargetIds.Add(node.TargetId))
				continue;
			if (!MatchesSelector(node, request.Selector))
				continue;
			if (expressionMatcher is not null && !expressionMatcher(node, initialSnapshot))
				continue;

			matches.Add(ToMatch(node, initialSnapshot));
		}

		foreach (var rootId in rootIdSet)
		{
			if (matches.Count >= maxMatches)
				break;

			var snapshot = treeService.CaptureSnapshot(new TreeSnapshotOptions
			{
				RootTargetId = rootId,
				RequestedPropertyNames = propertyNames,
				IncludeHidden = true,
				MaxDepth = request.MaxDepth,
				MaxNodeCount = request.MaxNodeCount ?? DefaultMaxNodeCount,
			});
			LogIfTruncated(snapshot, request.MaxNodeCount ?? DefaultMaxNodeCount, $"root-scoped find snapshot '{rootId}'");

			foreach (var node in snapshot.Nodes)
			{
				if (matches.Count >= maxMatches)
					break;
				if (!request.IncludeRoot && string.Equals(node.TargetId, rootId, StringComparison.Ordinal))
					continue;
				if (!seenTargetIds.Add(node.TargetId))
					continue;
				if (!MatchesSelector(node, request.Selector))
					continue;
				if (expressionMatcher is not null && !expressionMatcher(node, snapshot))
					continue;

				matches.Add(ToMatch(node, snapshot));
			}
		}

		return matches;
	}

	private static void LogIfTruncated(VisualTreeSnapshot snapshot, int maxNodeCount, string context)
	{
		if (!snapshot.IsTruncated)
			return;

		PayloadLog.Write($"FindElementCommand warning: {context} reached MaxNodeCount={maxNodeCount}. {snapshot.TruncationReason}");
	}

	private static FindElementMatchResponse ToMatch(VisualTreeNodeDto node, VisualTreeSnapshot snapshot) =>
		new()
		{
			TargetId = node.TargetId,
			TypeName = node.TypeName,
			FrameworkTypeName = node.FrameworkTypeName,
			Properties = node.Properties,
			Path = BuildPath(node, snapshot),
		};

	private static List<ElementPathSegmentResponse> BuildPath(VisualTreeNodeDto node, VisualTreeSnapshot snapshot)
	{
		var byId = BuildNodeLookup(snapshot.Nodes);
		List<ElementPathSegmentResponse> path = [];
		var seenTargetIds = new HashSet<string>(StringComparer.Ordinal);
		var current = node;
		while (true)
		{
			if (!seenTargetIds.Add(current.TargetId))
				break;

			path.Add(ToPathSegment(current));
			if (string.IsNullOrWhiteSpace(current.ParentId))
				break;
			if (!byId.TryGetValue(current.ParentId!, out current))
				break;
		}

		path.Reverse();
		return path;
	}

	private static ElementPathSegmentResponse ToPathSegment(VisualTreeNodeDto node) =>
		new()
		{
			TargetId = node.TargetId,
			TypeName = node.TypeName,
			FrameworkTypeName = node.FrameworkTypeName,
			Properties = node.Properties,
		};

	private static FindElementCommandResponse CreateResponse(IReadOnlyList<FindElementMatchResponse> matches, int maxMatches)
	{
		return new FindElementCommandResponse
		{
			Status = matches.Count == 0 ? ProtocolConstants.Statuses.NoMatch : ProtocolConstants.Statuses.Ok,
			Matches = matches.ToList(),
			MatchCount = matches.Count,
			MaxMatches = maxMatches,
		};
	}

	private static bool IsInRequestedRootScope(
		VisualTreeNodeDto node,
		FindElementCommandRequest request,
		ISet<string>? rootIds,
		IReadOnlyDictionary<string, string?>? parentIdsByTargetId)
	{
		if (!request.IncludeRoot
			&& !string.IsNullOrWhiteSpace(request.RootTargetId)
			&& string.Equals(node.TargetId, request.RootTargetId, StringComparison.Ordinal))
		{
			return false;
		}

		if (rootIds is null)
			return true;

		if (rootIds.Contains(node.TargetId))
			return request.IncludeRoot;

		var parentId = node.ParentId;
		while (!string.IsNullOrWhiteSpace(parentId))
		{
			if (rootIds.Contains(parentId!))
				return true;

			if (parentIdsByTargetId is null || !parentIdsByTargetId.TryGetValue(parentId!, out parentId))
				return false;
		}

		return false;
	}

	private static IReadOnlyDictionary<string, string?> BuildParentIdLookup(IEnumerable<VisualTreeNodeDto> nodes)
	{
		var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
		foreach (var node in nodes)
		{
			if (!parents.TryGetValue(node.TargetId, out var existingParentId) || string.IsNullOrWhiteSpace(existingParentId))
				parents[node.TargetId] = node.ParentId;
		}

		return parents;
	}

	private static IReadOnlyDictionary<string, VisualTreeNodeDto> BuildNodeLookup(IEnumerable<VisualTreeNodeDto> nodes)
	{
		var byId = new Dictionary<string, VisualTreeNodeDto>(StringComparer.Ordinal);
		foreach (var node in nodes)
		{
			if (!byId.TryGetValue(node.TargetId, out var existing) || existing.ParentId is null)
				byId[node.TargetId] = node;
		}

		return byId;
	}

	private static IEnumerable<string> GetRequestedPropertyNames(FindElementCommandRequest request)
	{
		var names = new HashSet<string>(request.PropNames ?? VisualTreePropertyExtractor.DefaultPropertyNames, StringComparer.Ordinal);
		if (request.Selector is not null)
		{
			if (!string.IsNullOrWhiteSpace(request.Selector.Name))
				names.Add(KnownProperties.Name);
			if (!string.IsNullOrWhiteSpace(request.Selector.AutomationId))
				names.Add(KnownProperties.AutomationId);
			if (!string.IsNullOrWhiteSpace(request.Selector.Text))
				names.Add(KnownProperties.Text);
			if (!string.IsNullOrWhiteSpace(request.Selector.Content))
				names.Add(KnownProperties.Content);

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

		if (!string.IsNullOrWhiteSpace(selector.Name) && !PropertyEquals(node, KnownProperties.Name, selector.Name))
			return false;

		if (!string.IsNullOrWhiteSpace(selector.AutomationId)
			&& !PropertyEquals(node, KnownProperties.AutomationId, selector.AutomationId)
			&& !PropertyEquals(node, KnownProperties.AutomationIdAlias, selector.AutomationId))
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(selector.Text) && !PropertyEquals(node, KnownProperties.Text, selector.Text))
			return false;

		if (!string.IsNullOrWhiteSpace(selector.Content) && !PropertyEquals(node, KnownProperties.Content, selector.Content))
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

	private static SnapshotNodeMatcher? TryCreateExpressionMatcher(object? matcherCode, string? matcherHash, ExpressionCache expressionCache)
	{
		if (matcherCode is null)
			return null;

		var payload = ToExpressionMatcherPayload(matcherCode);
		if (string.IsNullOrWhiteSpace(payload.ExpressionHash) && !string.IsNullOrWhiteSpace(matcherHash))
			payload.ExpressionHash = matcherHash!;
		if (string.IsNullOrWhiteSpace(payload.ExpressionHash))
			payload.ExpressionHash = ComputeSha256(payload.ExpressionJson);

		return expressionCache.GetOrCompile(payload, CompileExpressionMatcher);
	}

	private static ExpressionMatcherPayload ToExpressionMatcherPayload(object rawPayload)
	{
		if (rawPayload is ExpressionMatcherPayload payload)
			return payload;

		if (TryReadEvalExpressionJson(rawPayload, out var expressionJson))
		{
			return new ExpressionMatcherPayload
			{
				ExpressionJson = expressionJson,
				ExpressionHash = ComputeSha256(expressionJson),
			};
		}

		try
		{
			return MessagePacker.ConvertTo<ExpressionMatcherPayload>(rawPayload);
		}
		catch (Exception ex) when (ex is InvalidCastException or ArgumentException or ProtocolException or JsonException)
		{
			throw new InvalidOperationException("FindElement matcher must be an expression payload or p:Eval envelope.", ex);
		}
	}

	private static bool TryReadEvalExpressionJson(object rawPayload, out string expressionJson)
	{
		expressionJson = string.Empty;
		if (!TryGetValue(rawPayload, "Type", out var type) || !string.Equals(type?.ToString(), Eval.EvalType, StringComparison.Ordinal))
			return false;

		if (!TryGetValue(rawPayload, "ExpressionJson", out var value) || value is not string serializedExpression)
			throw new ArgumentException("Eval.ExpressionJson must be a string.");

		expressionJson = serializedExpression;
		return true;
	}

	private static SnapshotNodeMatcher CompileExpressionMatcher(ExpressionMatcherPayload payload)
	{
		if (string.IsNullOrWhiteSpace(payload.ExpressionJson))
			throw new InvalidOperationException("Expression matcher payload is empty.");

		var expression = ExpressionPayloadSerializer.Deserialize(payload.ExpressionJson);
		if (expression.Parameters.Count != 1)
			throw new InvalidOperationException("Expression matcher must have exactly one parameter.");

		var parameterType = expression.Parameters[0].Type;
		var compiled = expression.Compile();
		if (parameterType.IsAssignableFrom(typeof(VisualTreeNodeDto)))
		{
			return (node, _) =>
			{
				var result = compiled.DynamicInvoke(node);
				return result is bool boolResult && boolResult;
			};
		}

		if (typeof(Element).IsAssignableFrom(parameterType))
		{
			return (node, snapshot) =>
			{
				var element = CreatePayloadElement(parameterType, node, snapshot);
				var result = compiled.DynamicInvoke(element);
				return result is bool boolResult && boolResult;
			};
		}

		throw new InvalidOperationException("Expression matcher must compile to a VisualTreeNodeDto or Element predicate.");
	}

	private static Element CreatePayloadElement(Type elementType, VisualTreeNodeDto node, VisualTreeSnapshot snapshot)
	{
		var element = Element.FromSnapshot(node, snapshot);
		if (elementType == typeof(Element))
			return element;

		var constructor = elementType.GetConstructor(
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			binder: null,
			types: new[] { typeof(Element) },
			modifiers: null);
		if (constructor is null)
			throw new InvalidOperationException($"Element wrapper '{elementType.FullName}' must expose a constructor that accepts Element.");

		return (Element)constructor.Invoke(new object[] { element });
	}

	private static bool TryGetValue(object obj, string propertyName, out object? value)
	{
		if (obj is IDictionary<string, object?> genericDictionary)
			return genericDictionary.TryGetValue(propertyName, out value);

		if (obj is IDictionary dictionary)
		{
			foreach (var item in dictionary)
			{
				if (item is not DictionaryEntry entry)
					continue;

				if (string.Equals(entry.Key?.ToString(), propertyName, StringComparison.Ordinal))
				{
					value = entry.Value;
					return true;
				}
			}
		}

		if (obj is JObject jObject && jObject.TryGetValue(propertyName, StringComparison.Ordinal, out var token))
		{
			value = token.Type == JTokenType.Null ? null : token.ToObject<object?>();
			return true;
		}

		var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
		if (property is not null && property.GetIndexParameters().Length == 0)
		{
			value = property.GetValue(obj, null);
			return true;
		}

		value = null;
		return false;
	}

	private static string ComputeSha256(string text)
	{
		using var sha256 = SHA256.Create();
		var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
		return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
	}
}
