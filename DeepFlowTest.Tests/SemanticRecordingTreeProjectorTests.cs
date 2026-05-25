namespace DeepFlowTest.Tests;

using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class SemanticRecordingTreeProjectorTests
{
	[Test]
	public void InitialSnapshotPrunesStructuralNodesAndReparentsVisibleChildren()
	{
		var projector = CreateProjector();

		var projected = projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					Node("window-0001", "Window", isRoot: true, children: ["grid-0002"], automationId: "MainWindow"),
					Node("grid-0002", "Grid", parentId: "window-0001", children: ["border-0003"]),
					Node("border-0003", "Border", parentId: "grid-0002", children: ["button-0004"]),
					Node("button-0004", "Button", parentId: "border-0003", automationId: "SubmitButton", text: "Submit"),
					Node("canvas-0005", "Canvas", parentId: "grid-0002"),
					Node("content-0006", "ContentPresenter", parentId: "grid-0002"),
					Node("rectangle-0007", "Rectangle", parentId: "grid-0002"),
				]),
		});

		Assert.That(projected.Summary, Is.EqualTo("nodes 2/7"));
		Assert.That(projected.Roots, Has.Count.EqualTo(1));
		Assert.That(projected.Roots[0].Label, Is.EqualTo("Window [0001] #MainWindow"));
		Assert.That(projected.Roots[0].Children.Select(static node => node.Label), Is.EqualTo(new[] { "Button [0004] #SubmitButton text=\"Submit\"" }));
	}

	[Test]
	public void StructuralNodesWithAutomationIdArePreserved()
	{
		var projector = CreateProjector();

		var projected = projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					Node("window-0001", "Window", isRoot: true, children: ["canvas-0002"], automationId: "MainWindow"),
					Node("canvas-0002", "Canvas", parentId: "window-0001", automationId: "SemanticCanvas"),
				]),
		});

		Assert.That(projected.Roots[0].Children.Single().Label, Is.EqualTo("Canvas [0002] #SemanticCanvas"));
	}

	[Test]
	public void AddedAndChangedMarkersAreTransient()
	{
		var projector = CreateProjector();
		projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					Node("window-0001", "Window", isRoot: true, children: ["button-0002"], automationId: "MainWindow"),
					Node("button-0002", "Button", parentId: "window-0001", automationId: "SaveButton", text: "Save"),
				]),
		});

		var added = projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 2,
			Delta = new VisualTreeSnapshotDelta
			{
				Added = [Node("text-0003", "TextBox", parentId: "window-0001", automationId: "SearchBox", text: "query")],
			},
		});

		Assert.That(Find(added, "text-0003")!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.Added));
		Assert.That(added.Markers.Single().Kind, Is.EqualTo(SemanticRecordingChangeKind.Added));

		var changed = projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 3,
			Delta = new VisualTreeSnapshotDelta
			{
				Changed = [Node("button-0002", "Button", parentId: "window-0001", automationId: "SaveButton", text: "Saved")],
			},
		});

		var changedButton = Find(changed, "button-0002")!;
		Assert.That(changedButton.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.Changed));
		Assert.That(changedButton.ChangedProperties, Contains.Key("text"));
		Assert.That(Find(changed, "text-0003")!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.None));

		var next = projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "action",
			SequenceNumber = 4,
			Action = new RecordedInputAction { ActionKind = "click" },
		});

		Assert.That(Find(next, "button-0002")!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.None));
		Assert.That(Find(next, "text-0003")!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.None));
	}

	[Test]
	public void RemovedNodesAppearAsGhostOnlyOnRemovalFrame()
	{
		var projector = CreateProjector();
		projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					Node("window-0001", "Window", isRoot: true, children: ["button-0002"], automationId: "MainWindow"),
					Node("button-0002", "Button", parentId: "window-0001", automationId: "DeleteButton"),
				]),
		});

		var removed = projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 2,
			Delta = new VisualTreeSnapshotDelta
			{
				RemovedTargetIds = ["button-0002"],
			},
		});

		var ghost = Find(removed, "button-0002");
		Assert.That(ghost, Is.Not.Null);
		Assert.That(ghost!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.Removed));
		Assert.That(removed.Markers.Single().Kind, Is.EqualTo(SemanticRecordingChangeKind.Removed));

		var next = projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "action",
			SequenceNumber = 3,
			Action = new RecordedInputAction { ActionKind = "click" },
		});

		Assert.That(Find(next, "button-0002"), Is.Null);
	}

	[Test]
	public void DeltaSummaryCountsOnlyProjectedMarkers()
	{
		var projector = CreateProjector();
		projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					Node("window-0001", "Window", isRoot: true, children: ["grid-0002"], automationId: "MainWindow"),
					Node("grid-0002", "Grid", parentId: "window-0001"),
				]),
		});

		var delta = projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 2,
			Delta = new VisualTreeSnapshotDelta
			{
				Added = [Node("rectangle-0003", "Rectangle", parentId: "window-0001")],
				Changed = [Node("grid-0002", "Grid", parentId: "window-0001")],
				RemovedTargetIds = ["grid-0002"],
			},
		});

		Assert.That(delta.Summary, Is.EqualTo("+0 *0 -0"));
		Assert.That(delta.Markers, Is.Empty);
	}

	[Test]
	public void ChangedNodeWithoutCompactPropertyChangesDoesNotCreateMarker()
	{
		var projector = CreateProjector();
		projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					Node("window-0001", "Window", isRoot: true, children: ["button-0002"], automationId: "MainWindow"),
					Node("button-0002", "Button", parentId: "window-0001", automationId: "SaveButton", text: "Save"),
				]),
		});

		var delta = projector.Apply(new SemanticRecordingFrame
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
						unknownPropertyValue: "1,1,10,10"),
				],
			},
		});

		Assert.That(delta.Summary, Is.EqualTo("+0 *0 -0"));
		Assert.That(delta.Markers, Is.Empty);
		Assert.That(Find(delta, "button-0002")!.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.None));
	}

	[Test]
	public void ActionFramesPreserveTreeStateAndMarkActionTarget()
	{
		var projector = CreateProjector();
		projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					Node("window-0001", "Window", isRoot: true, children: ["button-0002"], automationId: "MainWindow"),
					Node("button-0002", "Button", parentId: "window-0001", automationId: "SaveButton"),
				]),
		});

		var action = projector.Apply(new SemanticRecordingFrame
		{
			FrameKind = "action",
			SequenceNumber = 2,
			Action = new RecordedInputAction
			{
				ActionKind = "click",
				Target = new RecordedTarget
				{
					TargetId = "button-0002",
					TypeName = "Button",
				},
			},
		});

		var target = Find(action, "button-0002")!;
		Assert.That(target.IsActionTarget, Is.True);
		Assert.That(target.ChangeKind, Is.EqualTo(SemanticRecordingChangeKind.None));
		Assert.That(action.ActionTargetId, Is.EqualTo("button-0002"));
	}

	private static SemanticRecordingTreeProjector CreateProjector() =>
		new(new SemanticRecordingFormattingOptions { PruneStructuralLayoutNodes = true });

	private static SemanticRecordingTreeNode? Find(SemanticRecordingTreeFrame frame, string targetId) =>
		frame.Roots.SelectMany(Flatten).SingleOrDefault(node => node.TargetId == targetId);

	private static System.Collections.Generic.IEnumerable<SemanticRecordingTreeNode> Flatten(SemanticRecordingTreeNode node)
	{
		yield return node;
		foreach (var child in node.Children.SelectMany(Flatten))
			yield return child;
	}

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
