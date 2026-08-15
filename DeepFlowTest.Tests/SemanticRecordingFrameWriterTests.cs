namespace DeepFlowTest.Tests;

using System;
using System.IO;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class SemanticRecordingFrameWriterTests
{
	[Test]
	public void CondensedAgentUsesShortIdsAndBooleanStateShorthand()
	{
		var output = new StringWriter();
		using (var writer = SemanticRecordingFrameWriter.Create(output, SemanticRecordingOutputFormat.CondensedAgent))
		{
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "snapshot",
				SequenceNumber = 1,
				TimestampUtc = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
				Snapshot = VisualTreeSnapshot.Create(
					7,
					[
						new VisualTreeNodeDto
						{
							TargetId = "dft-target-1",
							TypeName = "App",
							IsRoot = true,
						},
						new VisualTreeNodeDto
						{
							TargetId = "dft-target-3e",
							ParentId = "dft-target-1",
							TypeName = "ToggleButton",
							Properties =
							{
								[KnownProperties.AutomationId] = "AdvancedToggle",
								[KnownProperties.Name] = "AdvancedToggleName",
								[KnownProperties.IsChecked] = false,
								[KnownProperties.IsExpanded] = true,
								[KnownProperties.IsVisible] = false,
							},
						},
					]),
			});
		}

		var text = output.ToString();

		Assert.That(text, Does.StartWith("dft-condensed/1 profile=agent source=compact-json"));
		Assert.That(text, Does.Contain("@1 snapshot"));
		Assert.That(text, Does.Contain("treeSeq=7"));
		Assert.That(text, Does.Contain("ToggleButton [3e] #AdvancedToggle .AdvancedToggleName !visible !checked expanded"));
		Assert.That(text, Does.Not.Contain("[3e=dft-target-3e]"));
		Assert.That(text, Does.Not.Contain("checked=false"));
		Assert.That(text, Does.Not.Contain("expanded=true"));
	}

	[Test]
	public void CondensedAgentUsesSourceAsIdentityForUnnamedImages()
	{
		var output = new StringWriter();
		using (var writer = SemanticRecordingFrameWriter.Create(output, SemanticRecordingOutputFormat.CondensedDiagnostic))
		{
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "snapshot",
				SequenceNumber = 1,
				Snapshot = VisualTreeSnapshot.Create(
					1,
					[
						new VisualTreeNodeDto
						{
							TargetId = "dft-target-271a",
							TypeName = "Image",
							Properties =
							{
								[KnownProperties.Source] = "pack://application:,,,/Assets/toolbar-save.png",
								[KnownProperties.IsVisible] = false,
							},
						},
					]),
			});
		}

		Assert.That(output.ToString(), Does.Contain("Image [271a] source=\"pack://application:,,,/Assets/toolbar-save.png\" !visible"));
	}

	[Test]
	public void CondensedAgentWritesActionsSelectorsAndChangedState()
	{
		var output = new StringWriter();
		using (var writer = SemanticRecordingFrameWriter.Create(output, SemanticRecordingOutputFormat.CondensedAgent))
		{
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "snapshot",
				SequenceNumber = 1,
				Snapshot = VisualTreeSnapshot.Create(
					1,
					[
						new VisualTreeNodeDto
						{
							TargetId = "dft-target-3e",
							TypeName = "ToggleButton",
							Properties =
							{
								[KnownProperties.AutomationId] = "SaveButton",
								[KnownProperties.IsChecked] = false,
								[KnownProperties.IsExpanded] = true,
							},
						},
					]),
			});
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "action",
				SequenceNumber = 2,
				Action = new RecordedInputAction
				{
					ActionKind = "click",
					MouseButton = "left",
					ClickCount = 1,
					Target = new RecordedTarget
					{
						TargetId = "dft-target-3e",
						TypeName = "ToggleButton",
						Summary = "ToggleButton[AutomationId='SaveButton']",
						Properties =
						{
							[KnownProperties.AutomationId] = "SaveButton",
							[KnownProperties.IsChecked] = false,
						},
						SelectorHints =
						{
							new RecordedSelectorHint
							{
								Kind = "automation-id",
								Confidence = 0.98,
								PropertyName = KnownProperties.AutomationId,
								Value = "SaveButton",
								Cli = "--automation-id \"SaveButton\"",
							},
						},
					},
				},
			});
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "delta",
				SequenceNumber = 3,
				Delta = new VisualTreeSnapshotDelta
				{
					BaseSequenceNumber = 1,
					CurrentSequenceNumber = 2,
					Changed =
					[
						new VisualTreeNodeDto
						{
							TargetId = "dft-target-3e",
							TypeName = "ToggleButton",
							Properties =
							{
								[KnownProperties.AutomationId] = "SaveButton",
								[KnownProperties.IsChecked] = true,
								[KnownProperties.IsExpanded] = false,
							},
						},
					],
				},
			});
		}

		var text = output.ToString();

		Assert.That(text, Does.Contain("@2 action"));
		Assert.That(text, Does.Contain("> target ToggleButton [3e] #SaveButton !checked summary=\"ToggleButton[AutomationId='SaveButton']\""));
		Assert.That(text, Does.Contain("> input mouseButton=left clickCount=1"));
		Assert.That(text, Does.Contain("> selector automation-id property=automationId value=\"SaveButton\" confidence=0.98"));
		Assert.That(text, Does.Contain("@3 delta"));
		Assert.That(text, Does.Contain("* ToggleButton [3e] #SaveButton checked !expanded"));
	}

	[Test]
	public void CondensedDiagnosticWritesMouseWheelDelta()
	{
		var output = new StringWriter();
		using (var writer = SemanticRecordingFrameWriter.Create(output, SemanticRecordingOutputFormat.CondensedDiagnostic))
		{
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "action",
				SequenceNumber = 4,
				Action = new RecordedInputAction
				{
					ActionKind = "wheel",
					WheelDelta = -120,
					Target = new RecordedTarget { TargetId = "dft-target-271a", TypeName = "ScrollViewer" },
				},
			});
		}

		Assert.That(output.ToString(), Does.Contain("kind=wheel"));
		Assert.That(output.ToString(), Does.Contain("> input wheelDelta=-120"));
	}

	[Test]
	public void CondensedAgentDoesNotPruneStructuralLayoutNodesByDefault()
	{
		var output = new StringWriter();
		using (var writer = SemanticRecordingFrameWriter.Create(output, SemanticRecordingOutputFormat.CondensedAgent))
			writer.WriteFrame(CreateLayoutSnapshotFrame());

		var text = output.ToString();

		Assert.That(text, Does.Contain("Window [0001] #MainWindow"));
		Assert.That(text, Does.Contain("Grid [0002] .LayoutGrid"));
		Assert.That(text, Does.Contain("Button [0003] #SubmitButton"));
	}

	[Test]
	public void CondensedAgentPrunesStructuralLayoutNodesWhenOptionIsEnabled()
	{
		var output = new StringWriter();
		using (var writer = SemanticRecordingFrameWriter.Create(
			output,
			SemanticRecordingOutputFormat.CondensedAgent,
			new SemanticRecordingFormattingOptions { PruneStructuralLayoutNodes = true }))
		{
			writer.WriteFrame(CreateLayoutSnapshotFrame());
		}

		var text = output.ToString();

		Assert.That(text, Does.Contain("Window [0001] #MainWindow"));
		Assert.That(text, Does.Contain("Button [0003] #SubmitButton"));
		Assert.That(text, Does.Contain("Canvas [0004] #SemanticCanvas"));
		Assert.That(text, Does.Not.Contain("Grid [0002]"));
		Assert.That(text, Does.Not.Contain("LayoutGrid"));
	}

	[Test]
	public void CondensedDeltaCountsOnlyPrintedNodesAndOmitsPrunedCounts()
	{
		var output = new StringWriter();
		using (var writer = SemanticRecordingFrameWriter.Create(
			output,
			SemanticRecordingOutputFormat.CondensedAgent,
			new SemanticRecordingFormattingOptions { PruneStructuralLayoutNodes = true }))
		{
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "snapshot",
				SequenceNumber = 1,
				Snapshot = VisualTreeSnapshot.Create(
					1,
					[
						new VisualTreeNodeDto
						{
							TargetId = "grid-change-1",
							TypeName = "Grid",
							Properties =
							{
								[KnownProperties.Name] = "OldLayout",
							},
						},
						new VisualTreeNodeDto
						{
							TargetId = "content-change-2",
							TypeName = "ContentPresenter",
							Properties =
							{
								[KnownProperties.Name] = "OldContent",
							},
						},
						new VisualTreeNodeDto
						{
							TargetId = "button-remove-1",
							TypeName = "Button",
							Properties =
							{
								[KnownProperties.AutomationId] = "RemoveOne",
							},
						},
						new VisualTreeNodeDto
						{
							TargetId = "button-remove-2",
							TypeName = "Button",
							Properties =
							{
								[KnownProperties.AutomationId] = "RemoveTwo",
							},
						},
					]),
			});
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "delta",
				SequenceNumber = 2,
				Delta = new VisualTreeSnapshotDelta
				{
					BaseSequenceNumber = 1,
					CurrentSequenceNumber = 2,
					Added =
					[
						new VisualTreeNodeDto
						{
							TargetId = "grid-added-1",
							TypeName = "Grid",
							Properties =
							{
								[KnownProperties.Name] = "AddedLayout",
							},
						},
						new VisualTreeNodeDto
						{
							TargetId = "rectangle-added-2",
							TypeName = "Rectangle",
						},
					],
					Changed =
					[
						new VisualTreeNodeDto
						{
							TargetId = "grid-change-1",
							TypeName = "Grid",
							Properties =
							{
								[KnownProperties.Name] = "NewLayout",
							},
						},
						new VisualTreeNodeDto
						{
							TargetId = "content-change-2",
							TypeName = "ContentPresenter",
							Properties =
							{
								[KnownProperties.Name] = "NewContent",
							},
						},
					],
					RemovedTargetIds = ["button-remove-1", "button-remove-2"],
				},
			});
		}

		var text = output.ToString();
		var deltaText = text[(text.LastIndexOf("@2 delta", StringComparison.Ordinal))..];

		Assert.That(deltaText, Does.Contain("added=0 changed=0 removed=2"));
		Assert.That(deltaText, Does.Not.Contain("addedOmitted"));
		Assert.That(deltaText, Does.Not.Contain("changedOmitted"));
		Assert.That(deltaText, Does.Not.Contain("removedPruned"));
		Assert.That(deltaText, Does.Contain("- [1], [2]"));
		Assert.That(deltaText, Does.Not.Contain("+ "));
		Assert.That(deltaText, Does.Not.Contain("* "));
	}

	[Test]
	public void CondensedDeltaSkipsFrameWhenAllChangesArePruned()
	{
		var output = new StringWriter();
		using (var writer = SemanticRecordingFrameWriter.Create(
			output,
			SemanticRecordingOutputFormat.CondensedAgent,
			new SemanticRecordingFormattingOptions { PruneStructuralLayoutNodes = true }))
		{
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "snapshot",
				SequenceNumber = 1,
				Snapshot = VisualTreeSnapshot.Create(
					1,
					[
						new VisualTreeNodeDto
						{
							TargetId = "grid-change-1",
							TypeName = "Grid",
							Properties =
							{
								[KnownProperties.Name] = "OldLayout",
							},
						},
						new VisualTreeNodeDto
						{
							TargetId = "border-remove-1",
							TypeName = "Border",
							Properties =
							{
								[KnownProperties.Name] = "RemovedBorder",
							},
						},
					]),
			});
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "delta",
				SequenceNumber = 2,
				Delta = new VisualTreeSnapshotDelta
				{
					BaseSequenceNumber = 1,
					CurrentSequenceNumber = 2,
					Added =
					[
						new VisualTreeNodeDto
						{
							TargetId = "rectangle-added-1",
							TypeName = "Rectangle",
						},
					],
					Changed =
					[
						new VisualTreeNodeDto
						{
							TargetId = "grid-change-1",
							TypeName = "Grid",
							Properties =
							{
								[KnownProperties.Name] = "NewLayout",
							},
						},
					],
					RemovedTargetIds = ["border-remove-1"],
				},
			});
		}

		var text = output.ToString();

		Assert.That(text, Does.Not.Contain("@2 delta"));
		Assert.That(text, Does.Not.Contain("addedOmitted"));
		Assert.That(text, Does.Not.Contain("changedOmitted"));
		Assert.That(text, Does.Not.Contain("removedPruned"));
		Assert.That(text, Does.Not.Contain("[1]"));
	}

	[Test]
	public void CondensedDeltaSkipsFrameWhenDefaultCompactProjectionPrunesAllChanges()
	{
		var output = new StringWriter();
		using (var writer = SemanticRecordingFrameWriter.Create(output, SemanticRecordingOutputFormat.CondensedAgent))
		{
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "snapshot",
				SequenceNumber = 1,
				Snapshot = VisualTreeSnapshot.Create(
					1,
					[
						new VisualTreeNodeDto
						{
							TargetId = "border-remove-1",
							TypeName = "Border",
						},
					]),
			});
			writer.WriteFrame(new SemanticRecordingFrame
			{
				FrameKind = "delta",
				SequenceNumber = 2,
				Delta = new VisualTreeSnapshotDelta
				{
					BaseSequenceNumber = 1,
					CurrentSequenceNumber = 2,
					Added =
					[
						new VisualTreeNodeDto
						{
							TargetId = "rectangle-added-1",
							TypeName = "Rectangle",
						},
					],
					Changed =
					[
						new VisualTreeNodeDto
						{
							TargetId = "border-remove-1",
							TypeName = "Border",
						},
					],
					RemovedTargetIds = ["border-remove-1"],
				},
			});
		}

		var text = output.ToString();

		Assert.That(text, Does.Not.Contain("@2 delta"));
		Assert.That(text, Does.Not.Contain("- [1]"));
	}

	[Test]
	public void CompactJsonWriterStillWritesJsonWhenRequested()
	{
		var output = new StringWriter();
		using (var writer = SemanticRecordingFrameWriter.Create(output, SemanticRecordingOutputFormat.CompactJson))
		{
			writer.WriteFrame(new SemanticRecordingFrame
			{
				RecordingId = "recording",
				FrameKind = "recording-started",
				SequenceNumber = 1,
			});
		}

		var text = output.ToString();

		Assert.That(text, Does.StartWith("["));
		Assert.That(text, Does.Contain("\"kind\": \"recording-started\""));
		Assert.That(text, Does.Contain("\"recordingId\": \"recording\""));
	}

	[Test]
	public void SemanticRecordingOptionsDefaultToCondensedAgentButCompactOutputStillMapsToJson()
	{
		var options = new SemanticRecordingOptions();

		Assert.That(options.OutputFormat, Is.EqualTo(SemanticRecordingOutputFormat.CondensedAgent));
		Assert.That(options.CompactOutput, Is.False);

		var compactOptions = new SemanticRecordingOptions { CompactOutput = true };
		Assert.That(compactOptions.OutputFormat, Is.EqualTo(SemanticRecordingOutputFormat.CompactJson));
		Assert.That(compactOptions.CompactOutput, Is.True);

		var rawOptions = new SemanticRecordingOptions { CompactOutput = false };
		Assert.That(rawOptions.OutputFormat, Is.EqualTo(SemanticRecordingOutputFormat.RawJson));
	}

	private static SemanticRecordingFrame CreateLayoutSnapshotFrame() =>
		new()
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					new VisualTreeNodeDto
					{
						TargetId = "window-0001",
						IsRoot = true,
						TypeName = "Window",
						ChildIds = ["grid-0002"],
						Properties =
						{
							[KnownProperties.AutomationId] = "MainWindow",
							[KnownProperties.IsVisible] = true,
							[KnownProperties.IsEnabled] = true,
						},
					},
					new VisualTreeNodeDto
					{
						TargetId = "grid-0002",
						ParentId = "window-0001",
						TypeName = "Grid",
						ChildIds = ["button-0003", "canvas-0004"],
						Properties =
						{
							[KnownProperties.Name] = "LayoutGrid",
							[KnownProperties.IsVisible] = true,
							[KnownProperties.IsEnabled] = true,
						},
					},
					new VisualTreeNodeDto
					{
						TargetId = "button-0003",
						ParentId = "grid-0002",
						TypeName = "Button",
						Properties =
						{
							[KnownProperties.AutomationId] = "SubmitButton",
							[KnownProperties.Text] = "Submit",
							[KnownProperties.IsVisible] = true,
							[KnownProperties.IsEnabled] = true,
						},
					},
					new VisualTreeNodeDto
					{
						TargetId = "canvas-0004",
						ParentId = "grid-0002",
						TypeName = "Canvas",
						Properties =
						{
							[KnownProperties.AutomationId] = "SemanticCanvas",
							[KnownProperties.IsVisible] = true,
							[KnownProperties.IsEnabled] = true,
						},
					},
				]),
		};
}
