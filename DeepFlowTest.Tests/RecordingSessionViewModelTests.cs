namespace DeepFlowTest.Tests;

using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Recorder;
using NUnit.Framework;

[TestFixture]
public sealed class RecordingSessionViewModelTests
{
	[Test]
	public void IncomingSnapshotPopulatesCurrentRoots()
	{
		var viewModel = new RecordingSessionViewModel();

		viewModel.ReceiveBatch(Batch(SnapshotFrame()));

		Assert.That(viewModel.Frames, Has.Count.EqualTo(1));
		Assert.That(viewModel.CurrentRoots, Has.Count.EqualTo(1));
		Assert.That(viewModel.CurrentRoots[0].Label, Is.EqualTo("Window [0001] #MainWindow"));
		Assert.That(viewModel.FramePosition, Is.EqualTo("Frame 1/1"));
	}

	[Test]
	public void SelectingPreviousAndNextChangesOnlySelectedFrameHighlight()
	{
		var viewModel = new RecordingSessionViewModel();
		viewModel.ReceiveBatch(Batch(
			SnapshotFrame(),
			new SemanticRecordingFrame
			{
				FrameKind = "delta",
				SequenceNumber = 2,
				Delta = new VisualTreeSnapshotDelta
				{
					Changed = [Node("button-0002", "Button", parentId: "window-0001", automationId: "SaveButton", text: "Saved")],
				},
			},
			new SemanticRecordingFrame
			{
				FrameKind = "action",
				SequenceNumber = 3,
				Action = new RecordedInputAction { ActionKind = "click" },
			}));

		Assert.That(Find(viewModel, "button-0002")!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.None));

		viewModel.SelectFrame(viewModel.Frames[1]);

		Assert.That(viewModel.IsFollowingLatest, Is.False);
		Assert.That(Find(viewModel, "button-0002")!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.Changed));

		viewModel.SelectNext();

		Assert.That(viewModel.IsFollowingLatest, Is.True);
		Assert.That(Find(viewModel, "button-0002")!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.None));
	}

	[Test]
	public void JumpToLatestRestoresLiveFollow()
	{
		var viewModel = new RecordingSessionViewModel();
		viewModel.ReceiveBatch(Batch(SnapshotFrame()));
		viewModel.ReceiveBatch(Batch(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 2,
			Delta = new VisualTreeSnapshotDelta
			{
				Added = [Node("text-0003", "TextBox", parentId: "window-0001", automationId: "SearchBox")],
			},
		}));

		viewModel.SelectFrame(viewModel.Frames[0]);
		Assert.That(viewModel.IsFollowingLatest, Is.False);

		viewModel.JumpToLatest();

		Assert.That(viewModel.IsFollowingLatest, Is.True);
		Assert.That(viewModel.SelectedFrame, Is.SameAs(viewModel.Frames[1]));
		Assert.That(Find(viewModel, "text-0003")!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.Added));
	}

	[Test]
	public void TreeExpansionStateSurvivesLiveUpdates()
	{
		var viewModel = new RecordingSessionViewModel();
		viewModel.ReceiveBatch(Batch(SnapshotFrame()));

		Assert.That(viewModel.CurrentRoots[0].IsExpanded, Is.True);
		viewModel.CurrentRoots[0].IsExpanded = false;

		viewModel.ReceiveBatch(Batch(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 2,
			Delta = new VisualTreeSnapshotDelta
			{
				Changed = [Node("button-0002", "Button", parentId: "window-0001", automationId: "SaveButton", text: "Saved")],
			},
		}));

		Assert.That(viewModel.CurrentRoots[0].IsExpanded, Is.False);

		viewModel.CurrentRoots[0].IsExpanded = true;
		viewModel.ReceiveBatch(Batch(new SemanticRecordingFrame
		{
			FrameKind = "action",
			SequenceNumber = 3,
			Action = new RecordedInputAction { ActionKind = "click" },
		}));

		Assert.That(viewModel.CurrentRoots[0].IsExpanded, Is.True);
	}

	[Test]
	public void TreeSelectionStateSurvivesLiveUpdates()
	{
		var viewModel = new RecordingSessionViewModel();
		viewModel.ReceiveBatch(Batch(SnapshotFrame()));
		var originalSelection = Find(viewModel, "button-0002")!;

		viewModel.SelectedTreeNode = originalSelection;

		Assert.That(originalSelection.IsSelected, Is.True);

		viewModel.ReceiveBatch(Batch(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 2,
			Delta = new VisualTreeSnapshotDelta
			{
				Changed = [Node("button-0002", "Button", parentId: "window-0001", automationId: "SaveButton", text: "Saved")],
			},
		}));

		var updatedSelection = Find(viewModel, "button-0002")!;
		Assert.That(updatedSelection, Is.Not.SameAs(originalSelection));
		Assert.That(updatedSelection.IsSelected, Is.True);
		Assert.That(viewModel.SelectedTreeNode, Is.SameAs(updatedSelection));
		Assert.That(viewModel.SelectedTreeNode!.TargetId, Is.EqualTo("button-0002"));
	}

	[Test]
	public void EmptyProjectedDeltaUpdatesStateButDoesNotCreateVisibleFrame()
	{
		var viewModel = new RecordingSessionViewModel();
		viewModel.ReceiveBatch(Batch(SnapshotFrame()));

		viewModel.ReceiveBatch(Batch(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 2,
			Delta = new VisualTreeSnapshotDelta
			{
				Added = [Node("grid-0003", "Grid", parentId: "window-0001")],
			},
		}));

		Assert.That(viewModel.Frames, Has.Count.EqualTo(1));
		Assert.That(viewModel.SelectedFrame, Is.SameAs(viewModel.Frames[0]));
		Assert.That(viewModel.FramePosition, Is.EqualTo("Frame 1/1"));

		viewModel.ReceiveBatch(Batch(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 3,
			Delta = new VisualTreeSnapshotDelta
			{
				Added = [Node("button-0004", "Button", parentId: "grid-0003", automationId: "NestedButton")],
			},
		}));

		Assert.That(viewModel.Frames, Has.Count.EqualTo(2));
		Assert.That(viewModel.SelectedFrame, Is.SameAs(viewModel.Frames[1]));
		Assert.That(viewModel.CurrentRoots[0].Children.Single(static child => child.TargetId == "button-0004").ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.Added));

		viewModel.SelectFrame(viewModel.Frames[1]);

		Assert.That(viewModel.CurrentRoots[0].Children.Single(static child => child.TargetId == "button-0004").Label, Does.Contain("#NestedButton"));
	}

	[Test]
	public void ChangedDeltaWithNoSemanticPropertyDiffDoesNotCreateVisibleFrame()
	{
		var viewModel = new RecordingSessionViewModel();
		viewModel.ReceiveBatch(Batch(SnapshotFrame()));

		viewModel.ReceiveBatch(Batch(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 2,
			Delta = new VisualTreeSnapshotDelta
			{
				Changed =
				[
					Node(
						"button-0002",
						"Button",
						parentId: "window-0001",
						automationId: "SaveButton",
						text: "Save",
						unknownPropertyName: "RenderBounds",
						unknownPropertyValue: "2,2,20,20"),
				],
			},
		}));

		Assert.That(viewModel.Frames, Has.Count.EqualTo(1));
		Assert.That(viewModel.SelectedFrame, Is.SameAs(viewModel.Frames[0]));
		Assert.That(Find(viewModel, "button-0002")!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.None));
	}

	private static SemanticTreeNodeViewModel? Find(RecordingSessionViewModel viewModel, string targetId) =>
		viewModel.CurrentRoots.SelectMany(Flatten).SingleOrDefault(node => node.TargetId == targetId);

	private static System.Collections.Generic.IEnumerable<SemanticTreeNodeViewModel> Flatten(SemanticTreeNodeViewModel node)
	{
		yield return node;
		foreach (var child in node.Children.SelectMany(Flatten))
			yield return child;
	}

	private static SemanticRecordingBatch Batch(params SemanticRecordingFrame[] frames) =>
		new()
		{
			Frames = frames.ToList(),
		};

	private static SemanticRecordingFrame SnapshotFrame() =>
		new()
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					Node("window-0001", "Window", isRoot: true, children: ["button-0002"], automationId: "MainWindow"),
					Node("button-0002", "Button", parentId: "window-0001", automationId: "SaveButton", text: "Save"),
				]),
		};

	private static VisualTreeNodeDto Node(
		string targetId,
		string typeName,
		string? parentId = null,
		bool isRoot = false,
		string[]? children = null,
		string? automationId = null,
		string? text = null,
		string? unknownPropertyName = null,
		object? unknownPropertyValue = null)
	{
		var node = new VisualTreeNodeDto
		{
			TargetId = targetId,
			TypeName = typeName,
			ParentId = parentId,
			IsRoot = isRoot,
			ChildIds = children?.ToList() ?? [],
		};
		if (!string.IsNullOrWhiteSpace(automationId))
			node.Properties[KnownProperties.AutomationId] = automationId;
		if (!string.IsNullOrWhiteSpace(text))
			node.Properties[KnownProperties.Text] = text;
		if (!string.IsNullOrWhiteSpace(unknownPropertyName))
			node.Properties[unknownPropertyName] = unknownPropertyValue;

		return node;
	}
}
