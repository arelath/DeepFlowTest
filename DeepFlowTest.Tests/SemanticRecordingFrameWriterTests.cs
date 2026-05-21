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

		options.CompactOutput = true;

		Assert.That(options.OutputFormat, Is.EqualTo(SemanticRecordingOutputFormat.CompactJson));
		Assert.That(options.CompactOutput, Is.True);

		options.CompactOutput = false;

		Assert.That(options.OutputFormat, Is.EqualTo(SemanticRecordingOutputFormat.RawJson));
	}
}
