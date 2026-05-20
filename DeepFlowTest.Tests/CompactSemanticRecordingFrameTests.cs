namespace DeepFlowTest.Tests;

using System;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility.WpfUtility.Tree;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

[TestFixture]
public sealed class CompactSemanticRecordingFrameTests
{
	[Test]
	public void DeltaOutputOmitsNoisyVisualTreeInternalsAndMissingPropertyErrors()
	{
		var frame = new SemanticRecordingFrame
		{
			RecordingId = "recording",
			FrameKind = "delta",
			SequenceNumber = 5,
			TimestampUtc = new DateTimeOffset(2026, 5, 20, 4, 39, 33, TimeSpan.Zero),
			Delta = new VisualTreeSnapshotDelta
			{
				BaseSequenceNumber = 3,
				CurrentSequenceNumber = 6,
				Added =
				[
					new VisualTreeNodeDto
					{
						TargetId = "dft-target-save",
						ParentId = "dft-target-root",
						ChildIds = ["dft-target-child"],
						Depth = 42,
						SiblingIndex = 7,
						TypeName = "Button",
						FrameworkTypeName = "System.Windows.Controls.Button",
						TargetKind = "WpfVisual",
						RuntimeFamily = "wpf",
						CanReceiveActions = true,
						Hwnd = 123456,
						Properties =
						{
							[KnownProperties.Name] = string.Empty,
							[KnownProperties.AutomationId] = "SaveButton",
							[KnownProperties.Content] = "Save",
							[KnownProperties.Header] = "System.Windows.Controls.StackPanel",
							[KnownProperties.Text] = PropertyExtractionError.Missing(KnownProperties.Text),
							[KnownProperties.IsEnabled] = true,
							[KnownProperties.IsVisible] = true,
							[KnownProperties.Visibility] = "Visible",
						},
					},
				],
				Changed =
				[
					new VisualTreeNodeDto
					{
						TargetId = "dft-target-status",
						ParentId = "dft-target-root",
						TypeName = "TextBlock",
						Properties =
						{
							[KnownProperties.Text] = "Finished",
							[KnownProperties.IsVisible] = false,
						},
					},
				],
				RemovedTargetIds = ["dft-target-old"],
			},
		};

		var json = JsonConvert.SerializeObject(CompactSemanticRecordingFrame.Create(frame));

		Assert.That(json, Does.Contain("\"kind\":\"delta\""));
		Assert.That(json, Does.Contain("\"seq\":5"));
		Assert.That(json, Does.Contain("\"automationId\":\"SaveButton\""));
		Assert.That(json, Does.Contain("\"content\":\"Save\""));
		Assert.That(json, Does.Contain("\"text\":\"Finished\""));
		Assert.That(json, Does.Contain("\"visible\":false"));
		Assert.That(json, Does.Contain("\"removedCount\":1"));
		Assert.That(json, Does.Not.Contain("\"recordingId\""));
		Assert.That(json, Does.Not.Contain("\"depth\""));
		Assert.That(json, Does.Not.Contain("missing-property"));
		Assert.That(json, Does.Not.Contain("frameworkTypeName"));
		Assert.That(json, Does.Not.Contain("childIds"));
		Assert.That(json, Does.Not.Contain("runtimeFamily"));
		Assert.That(json, Does.Not.Contain("targetKind"));
		Assert.That(json, Does.Not.Contain("hwnd"));
		Assert.That(json, Does.Not.Contain("System.Windows.Controls.StackPanel"));
		Assert.That(json, Does.Not.Contain("\"enabled\":true"));
		Assert.That(json, Does.Not.Contain("\"visibility\":\"Visible\""));
	}

	[Test]
	public void SnapshotOutputKeepsUsefulNodesAndCountsOmittedLayoutNoise()
	{
		var frame = new SemanticRecordingFrame
		{
			RecordingId = "recording",
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					new VisualTreeNodeDto
					{
						TargetId = "dft-target-root",
						IsRoot = true,
						TypeName = "Window",
					},
					new VisualTreeNodeDto
					{
						TargetId = "layout-only",
						ParentId = "dft-target-root",
						TypeName = "StackPanel",
						Properties =
						{
							[KnownProperties.IsEnabled] = true,
							[KnownProperties.IsVisible] = true,
						},
					},
					new VisualTreeNodeDto
					{
						TargetId = "dft-target-ready",
						ParentId = "layout-only",
						TypeName = "TextBlock",
						Properties =
						{
							[KnownProperties.Text] = "Ready",
						},
					},
				]),
		};

		var json = JsonConvert.SerializeObject(CompactSemanticRecordingFrame.Create(frame));

		Assert.That(json, Does.Contain("\"nodeCount\":3"));
		Assert.That(json, Does.Contain("\"includedCount\":2"));
		Assert.That(json, Does.Contain("\"omittedCount\":1"));
		Assert.That(json, Does.Contain("\"id\":\"dft-target-root\""));
		Assert.That(json, Does.Contain("\"text\":\"Ready\""));
		Assert.That(json, Does.Contain("\"children\""));
		Assert.That(json, Does.Not.Contain("\"parent\""));
		Assert.That(json, Does.Not.Contain("layout-only"));

		var root = JObject.Parse(json)["snapshot"]?["nodes"]?.Single();
		Assert.That(root?["children"]?.Single()?["text"]?.Value<string>(), Is.EqualTo("Ready"));
	}

	[Test]
	public void RecordingStartedOutputKeepsRecordingIdForFileCorrelation()
	{
		var frame = new SemanticRecordingFrame
		{
			RecordingId = "recording",
			FrameKind = "recording-started",
			SequenceNumber = 1,
		};

		var json = JsonConvert.SerializeObject(CompactSemanticRecordingFrame.Create(frame));

		Assert.That(json, Does.Contain("\"kind\":\"recording-started\""));
		Assert.That(json, Does.Contain("\"recordingId\":\"recording\""));
	}

	[TestCase("action")]
	[TestCase("snapshot")]
	[TestCase("delta")]
	public void NonStartedOutputOmitsRecordingIdBecauseTheFileAlreadyScopesIt(string frameKind)
	{
		var frame = new SemanticRecordingFrame
		{
			RecordingId = "recording",
			FrameKind = frameKind,
			SequenceNumber = 2,
		};

		var json = JsonConvert.SerializeObject(CompactSemanticRecordingFrame.Create(frame));

		Assert.That(json, Does.Contain($"\"kind\":\"{frameKind}\""));
		Assert.That(json, Does.Not.Contain("\"recordingId\""));
	}

	[Test]
	public void DeltaChangedOutputShowsOnlyChangedPropertiesWhenPreviousSnapshotIsKnown()
	{
		var state = new CompactSemanticRecordingState();
		_ = CompactSemanticRecordingFrame.Create(new SemanticRecordingFrame
		{
			FrameKind = "snapshot",
			SequenceNumber = 1,
			Snapshot = VisualTreeSnapshot.Create(
				1,
				[
					new VisualTreeNodeDto
					{
						TargetId = "dft-target-input",
						TypeName = "TextBox",
						Properties =
						{
							[KnownProperties.AutomationId] = "HelloWorldInput",
							[KnownProperties.Text] = "Ready",
							[KnownProperties.IsEnabled] = true,
						},
					},
				]),
		}, state);

		var frame = new SemanticRecordingFrame
		{
			FrameKind = "delta",
			SequenceNumber = 2,
			Delta = new VisualTreeSnapshotDelta
			{
				BaseSequenceNumber = 1,
				CurrentSequenceNumber = 2,
				Changed =
				[
					new VisualTreeNodeDto
					{
						TargetId = "dft-target-input",
						TypeName = "TextBox",
						Properties =
						{
							[KnownProperties.AutomationId] = "HelloWorldInput",
							[KnownProperties.Text] = "HelloWorldButton_Click event triggered.",
							[KnownProperties.IsEnabled] = true,
						},
					},
				],
			},
		};

		var json = JsonConvert.SerializeObject(CompactSemanticRecordingFrame.Create(frame, state));
		var changed = JObject.Parse(json)["delta"]?["changed"]?.Single();

		Assert.That(changed?["id"]?.Value<string>(), Is.EqualTo("dft-target-input"));
		Assert.That(changed?["automationId"]?.Value<string>(), Is.EqualTo("HelloWorldInput"));
		Assert.That(changed?["text"], Is.Null);
		Assert.That(changed?["changes"]?["text"]?.Value<string>(), Is.EqualTo("HelloWorldButton_Click event triggered."));
		Assert.That(changed?["changes"]?["automationId"], Is.Null);
		Assert.That(changed?["changes"]?["enabled"], Is.Null);
	}
}
