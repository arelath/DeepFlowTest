namespace DeepFlowTest.Cli.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class WaitEngineTests
{
	[Test]
	public async Task ElementConditionsShareCentralEvaluationSemantics()
	{
		var session = new FakeCliAppSession();
		var source = new StubObservationSource();
		var matching = new FixedMatcher(Result(new Dictionary<string, object?>
		{
			["State"] = "Ready",
			[KnownProperties.IsVisible] = false,
			[KnownProperties.IsEnabled] = true,
		}));
		var absent = new FixedMatcher(new FindResultData { MatchCount = 0, MaxMatches = 10 });
		WaitCondition[] conditions =
		[
			new ElementExistsWaitCondition(matching),
			new ElementAbsentWaitCondition(absent),
			new ElementExactCountWaitCondition(matching, 1),
			new ElementMinimumCountWaitCondition(matching, 1),
			new ElementPropertyEqualsWaitCondition(matching, "State", "ready"),
			new ElementPropertyDiffersWaitCondition(matching, "Missing", "value"),
			new ElementEnabledWaitCondition(matching),
			new ElementDisabledWaitCondition(new FixedMatcher(Result(new Dictionary<string, object?> { [KnownProperties.IsEnabled] = false }))),
			new ElementVisibleWaitCondition(new FixedMatcher(Result(new Dictionary<string, object?> { [KnownProperties.IsVisible] = "true" }))),
			new ElementHiddenWaitCondition(matching),
		];

		foreach (var condition in conditions)
		{
			var result = await new WaitEngine().WaitAsync(
				session,
				new WaitRequest(condition, source, 100, 1, SnapshotRequest()));

			Assert.That(result.Condition, Is.EqualTo(condition.Kind));
			Assert.That(result.MatchResult, Is.Not.Null);
		}
	}

	[Test]
	public async Task NonElementConditionsUseTheSameAsyncEngine()
	{
		var session = new FakeCliAppSession();
		var source = new StubObservationSource { WindowTitles = new Queue<string?>(["New title"]) };
		var responsive = await new WaitEngine().WaitAsync(
			session,
			new WaitRequest(new ResponsiveWaitCondition(), source, 100, 1));
		var title = await new WaitEngine().WaitAsync(
			session,
			new WaitRequest(new WindowTitleChangedWaitCondition("Old title"), source, 100, 1));
		var stable = await new WaitEngine().WaitAsync(
			session,
			new WaitRequest(new StableWaitCondition(1, new FixedFingerprint()), source, 100, 1, SnapshotRequest()));

		Assert.That(responsive.Condition, Is.EqualTo(WaitConditionKind.Responsive));
		Assert.That(title.WindowTitle, Is.EqualTo("New title"));
		Assert.That(stable.Condition, Is.EqualTo(WaitConditionKind.Stable));
	}

	[Test]
	public async Task CancellationInterruptsPollingDelayWithoutBlockingAThread()
	{
		var source = new StubObservationSource();
		using var cancellation = new CancellationTokenSource();
		var task = new WaitEngine().WaitAsync(
			new FakeCliAppSession(),
			new WaitRequest(
				new ElementExistsWaitCondition(new FixedMatcher(new FindResultData())),
				source,
				5_000,
				5_000,
				SnapshotRequest()),
			cancellation.Token);

		await source.Observed.Task.WaitAsync(TimeSpan.FromSeconds(1));
		Assert.That(task.IsCompleted, Is.False);
		cancellation.Cancel();

		Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
	}

	[Test]
	public async Task CancellationInterruptsAnInFlightTransportObservation()
	{
		var session = new BlockingAsyncSession();
		using var cancellation = new CancellationTokenSource();
		var task = new WaitEngine().WaitAsync(
			session,
			new WaitRequest(new ResponsiveWaitCondition(), new StubObservationSource(), 5_000, 5_000),
			cancellation.Token);

		await session.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
		Assert.That(task.IsCompleted, Is.False);
		cancellation.Cancel();

		Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
	}

	[Test]
	public void TimeoutUsesTheSharedCommandTimeoutError()
	{
		var task = new WaitEngine().WaitAsync(
			new FakeCliAppSession(),
			new WaitRequest(
				new ElementExistsWaitCondition(new FixedMatcher(new FindResultData())),
				new StubObservationSource(),
				1,
				1,
				SnapshotRequest()));

		var error = Assert.ThrowsAsync<AutomationException>(async () => await task);
		Assert.That(error!.ErrorCode, Is.EqualTo(AutomationErrorCodes.CommandTimeout));
	}

	private static WaitSnapshotRequest SnapshotRequest() => new([KnownProperties.Name], 10);

	private static FindResultData Result(Dictionary<string, object?> properties) => new()
	{
		MatchCount = 1,
		MaxMatches = 10,
		Matches = [new FindMatchData { Node = new TreeNodeData { TargetId = "node", Properties = properties } }],
	};

	private sealed class FixedMatcher(FindResultData result) : IWaitTargetMatcher
	{
		public FindResultData Find(VisualTreeSnapshot snapshot) => result;
	}

	private sealed class FixedFingerprint : IWaitSnapshotFingerprint
	{
		public string Compute(VisualTreeSnapshot snapshot) => "stable";
	}

	private sealed class BlockingAsyncSession : IAutomationSession
	{
		public HelloCommandResponse Hello { get; } = new();

		public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TResponse Send<TResponse>(IpcCommand command, int timeoutMs) =>
			throw new InvalidOperationException("The wait engine must use the asynchronous session path.");

		public async Task<TResponse> SendAsync<TResponse>(
			IpcCommand command,
			int timeoutMs,
			CancellationToken cancellationToken = default)
		{
			Started.TrySetResult(true);
			await Task.Delay(Timeout.Infinite, cancellationToken);
			throw new InvalidOperationException("The canceled observation unexpectedly completed.");
		}

		public IAutomationStreamSession StartStream(StartSendingCommandRequest command, int timeoutMs) =>
			throw new NotSupportedException();

		public void Dispose()
		{
		}
	}

	private sealed class StubObservationSource : IWaitObservationSource
	{
		public TaskCompletionSource<bool> Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Queue<string?> WindowTitles { get; set; } = new(["Window"]);

		public long? LatestRevision => 1;

		public Task<VisualTreeSnapshot> ReadSnapshotAsync(
			IAutomationSession session,
			WaitSnapshotRequest request,
			int commandTimeoutMs,
			CancellationToken cancellationToken)
		{
			Observed.TrySetResult(true);
			return Task.FromResult(VisualTreeSnapshot.Create(1, []));
		}

		public string? ReadWindowTitle() => WindowTitles.Count > 0 ? WindowTitles.Dequeue() : null;
	}
}
