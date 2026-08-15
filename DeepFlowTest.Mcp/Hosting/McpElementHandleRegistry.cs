namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DeepFlowTest.Automation;
using DeepFlowTest.Interop;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Contracts;

internal sealed class McpElementHandleRegistry
{
	private readonly object gate = new();
	private readonly Dictionary<string, HandleEntry> entries = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> handlesByTarget = new(StringComparer.Ordinal);
	private long nextHandle;

	public HandleEntry Register(string contextId, string targetId, ElementSelector selector, TreeNodeData node, long revision)
	{
		var targetKey = contextId + "\0" + targetId;
		lock (gate)
		{
			if (handlesByTarget.TryGetValue(targetKey, out var existing) && entries.TryGetValue(existing, out var current))
				return current;

			var handle = "e" + Interlocked.Increment(ref nextHandle).ToString(System.Globalization.CultureInfo.InvariantCulture);
			var entry = new HandleEntry(handle, contextId, targetId, Clone(selector), node, revision);
			entries[handle] = entry;
			handlesByTarget[targetKey] = handle;
			return entry;
		}
	}

	public HandleResolution Resolve(string contextId, string handle, VisualTreeSnapshot snapshot)
	{
		HandleEntry entry;
		lock (gate)
		{
			if (!entries.TryGetValue(handle, out entry!) || !string.Equals(entry.ContextId, contextId, StringComparison.Ordinal))
				throw new AutomationException(AutomationErrorCodes.TargetNotFound, $"Element handle '{handle}' does not belong to context '{contextId}'.");
		}

		if (snapshot.Nodes.Any(node => string.Equals(node.TargetId, entry.TargetId, StringComparison.Ordinal)))
			return new HandleResolution(entry, entry.TargetId, "handle_exact", 1.0, snapshot.SequenceNumber);

		var found = Find(snapshot, entry.Selector);
		if (found.MatchCount == 0)
			throw new AutomationException(
				AutomationErrorCodes.StaleTarget,
				$"Element '{handle}' is stale and no replacement matched its selector.",
				new McpStaleElementDetails
				{
					Handle = handle,
					OriginalRevision = entry.OriginalRevision,
					CurrentRevision = snapshot.SequenceNumber,
					Selector = ToAgentSelector(entry.Selector),
				});

		var ranked = found.Matches
			.Select(match => new RankedMatch(match.Node, Score(entry.OriginalNode, match.Node)))
			.OrderByDescending(static match => match.Score)
			.ToArray();
		if (ranked.Length > 1 && ranked[0].Score == ranked[1].Score)
			throw CreateAmbiguousRepair(contextId, handle, entry, ranked, snapshot.SequenceNumber);

		var repaired = ranked[0];
		var confidence = Confidence(repaired.Score);
		var updated = entry with { TargetId = repaired.Node.TargetId, OriginalNode = repaired.Node };
		lock (gate)
		{
			handlesByTarget.Remove(contextId + "\0" + entry.TargetId);
			entries[handle] = updated;
			handlesByTarget[contextId + "\0" + repaired.Node.TargetId] = handle;
		}

		return new HandleResolution(updated, repaired.Node.TargetId, RepairStrategy(entry.OriginalNode, repaired.Node), confidence, snapshot.SequenceNumber);
	}

	public void RemoveContext(string contextId)
	{
		lock (gate)
		{
			var handles = entries.Values
				.Where(entry => string.Equals(entry.ContextId, contextId, StringComparison.Ordinal))
				.Select(entry => entry.Handle)
				.ToArray();
			foreach (var handle in handles)
				entries.Remove(handle);

			var targetKeys = handlesByTarget
				.Where(pair => handles.Contains(pair.Value, StringComparer.Ordinal))
				.Select(pair => pair.Key)
				.ToArray();
			foreach (var targetKey in targetKeys)
				handlesByTarget.Remove(targetKey);
		}
	}

	public string? TryGetHandle(string contextId, string targetId)
	{
		lock (gate)
			return handlesByTarget.TryGetValue(contextId + "\0" + targetId, out var handle) ? handle : null;
	}

	private static ElementSelector Clone(ElementSelector selector) =>
		new()
		{
			TargetId = null,
			TypeName = selector.TypeName,
			TypeContains = selector.TypeContains,
			Name = selector.Name,
			AutomationId = selector.AutomationId,
			Text = selector.Text,
			PropertyEquals = selector.PropertyEquals,
			PropertyContains = selector.PropertyContains,
			PropertyRegex = selector.PropertyRegex,
			Visible = selector.Visible,
			Enabled = selector.Enabled,
			CaseSensitive = selector.CaseSensitive,
			Index = selector.Index,
			First = false,
		};

	private static McpSemanticSelector ToAgentSelector(ElementSelector selector) =>
		new()
		{
			Type = selector.TypeName,
			TypeContains = selector.TypeContains,
			Name = selector.Name,
			AutomationId = selector.AutomationId,
			Text = selector.Text,
			PropertyEquals = ToProperty(selector.PropertyEquals),
			PropertyContains = ToProperty(selector.PropertyContains),
			PropertyRegex = ToProperty(selector.PropertyRegex),
			Visible = selector.Visible,
			Enabled = selector.Enabled,
			CaseSensitive = selector.CaseSensitive,
		};

	private static McpPropertyMatch? ToProperty(KeyValuePair<string, string>? property) =>
		property is { } value ? new McpPropertyMatch { Name = value.Key, Value = value.Value } : null;

	private FindResultData Find(VisualTreeSnapshot snapshot, ElementSelector selector) =>
		new FindSnapshotService().Find(snapshot, new FindSnapshotOptions
		{
			TypeName = selector.TypeName,
			TypeContains = selector.TypeContains,
			Name = selector.Name,
			AutomationId = selector.AutomationId,
			Text = selector.Text,
			PropertyEquals = selector.PropertyEquals,
			PropertyContains = selector.PropertyContains,
			PropertyRegex = selector.PropertyRegex,
			Visible = selector.Visible,
			Enabled = selector.Enabled,
			CaseSensitive = selector.CaseSensitive,
			Limit = 1_000,
			IncludePath = true,
			IncludeProperties = true,
			Properties = snapshot.RequestedPropertyNames,
			UseShortIds = true,
		});

	private AutomationException CreateAmbiguousRepair(string contextId, string handle, HandleEntry entry, IReadOnlyList<RankedMatch> matches, long revision)
	{
		var candidates = matches.Take(20).Select(match =>
		{
			var candidateSelector = StableSelector(match.Node);
			var candidate = Register(contextId, match.Node.TargetId, candidateSelector, match.Node, revision);
			return new McpAmbiguousElementCandidate
			{
				Handle = candidate.Handle,
				TargetId = match.Node.TargetId,
				Type = match.Node.TypeName,
				AutomationId = Property(match.Node, KnownProperties.AutomationId),
				Name = Property(match.Node, KnownProperties.AutomationName) ?? Property(match.Node, KnownProperties.Name),
				Text = KnownProperties.TextualIdentityPropertyNames.Select(property => Property(match.Node, property)).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)),
				Path = match.Node.Path,
			};
		}).ToArray();
		return new AutomationException(
			AutomationErrorCodes.AmbiguousTarget,
			$"Element '{handle}' is stale and repair matched {matches.Count} equally ranked replacements.",
			new McpAmbiguousElementDetails { MatchCount = matches.Count, Candidates = candidates });
	}

	private static ElementSelector StableSelector(TreeNodeData node)
	{
		var automationId = Property(node, KnownProperties.AutomationId);
		if (!string.IsNullOrWhiteSpace(automationId))
			return new ElementSelector { AutomationId = automationId, TypeName = node.TypeName };
		var automationName = Property(node, KnownProperties.AutomationName);
		if (!string.IsNullOrWhiteSpace(automationName))
			return new ElementSelector { Name = automationName, TypeName = node.TypeName };
		var name = Property(node, KnownProperties.Name);
		return new ElementSelector { Name = name, TypeName = node.TypeName };
	}

	private static int Score(TreeNodeData original, TreeNodeData candidate)
	{
		var score = string.Equals(original.TypeName, candidate.TypeName, StringComparison.Ordinal) ? 10 : 0;
		if (PropertyEquals(original, candidate, KnownProperties.AutomationId))
			score += 100;
		if (PropertyEquals(original, candidate, KnownProperties.AutomationName))
			score += 100;
		if (PropertyEquals(original, candidate, KnownProperties.Name))
			score += 50;
		if (PropertyEquals(original, candidate, "ActualWidth"))
			score += 50;
		if (PropertyEquals(original, candidate, "ActualHeight"))
			score += 50;
		return score;
	}

	private static string RepairStrategy(TreeNodeData original, TreeNodeData candidate)
	{
		if (PropertyEquals(original, candidate, KnownProperties.AutomationId))
			return "repaired_by_automation_id";
		if (PropertyEquals(original, candidate, KnownProperties.AutomationName))
			return "repaired_by_automation_name";
		if (PropertyEquals(original, candidate, KnownProperties.Name))
			return "repaired_by_name";
		return "repaired_by_selector";
	}

	private static double Confidence(int score) => Math.Min(0.99, Math.Max(0.5, score / 110.0));

	private static bool PropertyEquals(TreeNodeData left, TreeNodeData right, string property) =>
		string.Equals(Property(left, property), Property(right, property), StringComparison.Ordinal)
		&& !string.IsNullOrWhiteSpace(Property(left, property));

	private static string? Property(TreeNodeData node, string property) =>
		node.Properties.TryGetValue(property, out var value)
			? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
			: null;

	private sealed record RankedMatch(TreeNodeData Node, int Score);
}

internal sealed record HandleEntry(string Handle, string ContextId, string TargetId, ElementSelector Selector, TreeNodeData OriginalNode, long OriginalRevision);

internal sealed record HandleResolution(HandleEntry Entry, string TargetId, string Strategy, double Confidence, long CurrentRevision);
