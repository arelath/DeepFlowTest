namespace DeepFlowTest.Automation;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed record WaitRequest(
	WaitCondition Condition,
	IWaitObservationSource ObservationSource,
	int TimeoutMs,
	int IntervalMs,
	WaitSnapshotRequest? Snapshot = null);

public sealed record WaitSnapshotRequest(
	IReadOnlyList<string> Properties,
	int MaxNodeCount,
	bool IncludeHidden = true);

public sealed record WaitResult
{
	public WaitConditionKind Condition { get; init; }

	public long ElapsedMs { get; init; }

	public long? Revision { get; init; }

	public int MatchCount { get; init; }

	public FindResultData? MatchResult { get; init; }

	public string? WindowTitle { get; init; }
}

public interface IWaitObservationSource
{
	Task<VisualTreeSnapshot> ReadSnapshotAsync(
		IAutomationSession session,
		WaitSnapshotRequest request,
		int commandTimeoutMs,
		CancellationToken cancellationToken);

	string? ReadWindowTitle();

	long? LatestRevision { get; }
}

public sealed class SessionWaitObservationSource : IWaitObservationSource
{
	public long? LatestRevision { get; private set; }

	public async Task<VisualTreeSnapshot> ReadSnapshotAsync(
		IAutomationSession session,
		WaitSnapshotRequest request,
		int commandTimeoutMs,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(request);
		var timeout = Math.Max(1, commandTimeoutMs);
		var response = await session.SendAsync<object>(
			new GetVisualTreeCommandRequest
			{
				PropNames = request.Properties,
				AsSnapshot = true,
				IncludeHidden = request.IncludeHidden,
				MaxNodeCount = Math.Max(1, request.MaxNodeCount),
				TimeoutMs = timeout,
			},
			timeout,
			cancellationToken).ConfigureAwait(false);
		var snapshot = new VisualTreeResponseReader().Read(response, request.Properties);
		LatestRevision = snapshot.SequenceNumber;
		return snapshot;
	}

	public string? ReadWindowTitle() =>
		throw new AutomationException(AutomationErrorCodes.InvalidArguments, "This wait source does not provide window titles.");
}
