namespace DeepFlowTest.Cli;

using System;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class ElementResolver
{
	private readonly CliTargetIdService targetIds;
	private readonly FindSnapshotService findService;

	public ElementResolver(CliTargetIdService? targetIds = null, FindSnapshotService? findService = null)
	{
		this.targetIds = targetIds ?? new CliTargetIdService();
		this.findService = findService ?? new FindSnapshotService();
	}

	public ElementResolution Resolve(VisualTreeSnapshot snapshot, ElementSelector selector)
	{
		_ = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
		_ = selector ?? throw new ArgumentNullException(nameof(selector));

		if (!string.IsNullOrWhiteSpace(selector.TargetId))
		{
			var fullId = targetIds.Resolve(selector.TargetId!, snapshot);
			var node = snapshot.Nodes.First(node => node.TargetId == fullId);
			return ToResolution(snapshot, node);
		}

		if (selector.IsEmpty)
			throw new CliException(CliErrorCodes.InvalidArguments, "An element target selector is required.");

		var result = findService.Find(snapshot, new FindSnapshotOptions
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
			Limit = 1000,
			IncludePath = true,
			IncludeProperties = true,
			UseShortIds = true,
		});

		if (result.MatchCount == 0)
			throw new CliException(CliErrorCodes.NoMatch, "No matching element was found.");

		if (selector.Index.HasValue)
		{
			if (selector.Index.Value < 0 || selector.Index.Value >= result.Matches.Count)
				throw new CliException(CliErrorCodes.NoMatch, $"Element index {selector.Index.Value} is outside the match set.");

			return ToResolution(snapshot, result.Matches[selector.Index.Value].Node.TargetId);
		}

		if (selector.First)
			return ToResolution(snapshot, result.Matches[0].Node.TargetId);

		if (result.MatchCount > 1)
		{
			var candidates = result.Matches.Select(static match => new
			{
				match.Node.TargetId,
				match.Node.ShortId,
				match.Node.TypeName,
				Name = match.Node.Properties.TryGetValue(KnownProperties.Name, out var name) ? name : null,
				AutomationName = match.Node.Properties.TryGetValue(KnownProperties.AutomationName, out var automationName) ? automationName : null,
				Text = match.Node.Properties.TryGetValue(KnownProperties.Text, out var text) ? text : null,
				Content = match.Node.Properties.TryGetValue(KnownProperties.Content, out var content) ? content : null,
				match.Node.Path,
			}).ToArray();
			throw new CliException(CliErrorCodes.AmbiguousTarget, "Multiple elements matched the selector.", candidates);
		}

		return ToResolution(snapshot, result.Matches[0].Node.TargetId);
	}

	private ElementResolution ToResolution(VisualTreeSnapshot snapshot, string targetId)
	{
		var node = snapshot.Nodes.First(node => node.TargetId == targetId);
		return ToResolution(snapshot, node);
	}

	private ElementResolution ToResolution(VisualTreeSnapshot snapshot, VisualTreeNodeDto node)
	{
		var data = new TreeSnapshotService(targetIds).ShapeOne(node, snapshot, new TreeSnapshotOptions
		{
			IncludePath = true,
			IncludeTypeNames = true,
			UseShortIds = true,
		});
		return new ElementResolution
		{
			TargetId = node.TargetId,
			Summary = data,
		};
	}
}

public sealed class ElementResolution
{
	public string TargetId { get; set; } = string.Empty;

	public TreeNodeData Summary { get; set; } = new();
}
