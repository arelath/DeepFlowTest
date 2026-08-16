namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Mcp.Contracts;

internal sealed class McpWaitObservationSource(
	McpSession session,
	McpSnapshotCache cache) : IWaitObservationSource
{
	private readonly McpSession session = session ?? throw new ArgumentNullException(nameof(session));
	private readonly McpSnapshotCache cache = cache ?? throw new ArgumentNullException(nameof(cache));

	public long? LatestRevision => cache.GetLatestRevision(session.SessionId);

	public Task<VisualTreeSnapshot> ReadSnapshotAsync(
		IAutomationSession automationSession,
		WaitSnapshotRequest request,
		int commandTimeoutMs,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(automationSession);
		ArgumentNullException.ThrowIfNull(request);
		if (!ReferenceEquals(automationSession, session.AppSession))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Wait session does not match the MCP context.");

		return cache.GetOrRefreshAsync(
			session,
			request.Properties,
			request.MaxNodeCount,
			includeHidden: request.IncludeHidden,
			refresh: true,
			commandTimeoutMs: commandTimeoutMs,
			cancellationToken: cancellationToken);
	}

	public string? ReadWindowTitle() => session.GetMainWindowTitle();
}

internal sealed class McpWaitTargetMatcher(
	string contextId,
	McpAgentSelector target,
	McpElementHandleRegistry handles,
	int limit) : IWaitTargetMatcher
{
	private readonly string contextId = string.IsNullOrWhiteSpace(contextId)
		? throw new ArgumentException("A context ID is required.", nameof(contextId))
		: contextId;
	private readonly McpAgentSelector target = target ?? throw new ArgumentNullException(nameof(target));
	private readonly McpElementHandleRegistry handles = handles ?? throw new ArgumentNullException(nameof(handles));
	private readonly int limit = Math.Max(0, limit);

	public FindResultData Find(VisualTreeSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		if (!string.IsNullOrWhiteSpace(target.Handle))
		{
			try
			{
				var resolved = handles.Resolve(contextId, target.Handle!, snapshot);
				var node = snapshot.Nodes.FirstOrDefault(node => string.Equals(node.TargetId, resolved.TargetId, StringComparison.Ordinal));
				if (node is null)
					return EmptyHandleResult();
				var shaped = new TreeSnapshotService().ShapeOne(node, snapshot, new TreeSnapshotOptions
				{
					IncludePath = true,
					IncludeTypeNames = true,
					Properties = snapshot.RequestedPropertyNames,
					UseShortIds = true,
				});
				return new FindResultData
				{
					MatchCount = 1,
					MaxMatches = 1,
					Matches = [new FindMatchData { Node = shaped }],
				};
			}
			catch (AutomationException ex) when (ex.ErrorCode is AutomationErrorCodes.NoMatch or AutomationErrorCodes.TargetNotFound)
			{
				return EmptyHandleResult();
			}
		}

		var found = FindMatches(snapshot, target.ToAutomationSelector());
		if (found.MatchCount == 0 && target is McpSemanticSelector { Fallback: not null } semantic)
			return FindMatches(snapshot, semantic.Fallback.ToAutomationSelector());
		return found;
	}

	private FindResultData FindMatches(VisualTreeSnapshot snapshot, ElementSelector selector) =>
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
			Limit = limit,
			IncludePath = true,
			IncludeProperties = true,
			Properties = snapshot.RequestedPropertyNames,
			UseShortIds = true,
		});

	private static FindResultData EmptyHandleResult() => new() { MatchCount = 0, MaxMatches = 1 };
}

internal sealed class McpStableSnapshotFingerprint : IWaitSnapshotFingerprint
{
	public string Compute(VisualTreeSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		var semantic = McpSemanticRecordingFormatter.FormatSnapshot(snapshot).Text;
		return string.Join('\n', semantic
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Where(static line => !line.StartsWith("dft-condensed/", StringComparison.Ordinal)
				&& !line.StartsWith("@1 snapshot ", StringComparison.Ordinal))
			.Select(static line => Regex.Replace(line, @" \[[0-9a-f]+\]", string.Empty)));
	}
}
