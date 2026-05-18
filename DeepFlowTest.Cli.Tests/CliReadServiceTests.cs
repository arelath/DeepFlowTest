namespace DeepFlowTest.Cli.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class CliReadServiceTests
{
	[Test]
	public void VisualTreeReaderConvertsLegacyNodeList()
	{
		var nodes = new[] { Node("root-0001", isRoot: true) };

		var snapshot = new VisualTreeResponseReader().Read(nodes);

		Assert.That(snapshot.NodeCount, Is.EqualTo(1));
		Assert.That(snapshot.RootIds, Is.EqualTo(new[] { "root-0001" }));
	}

	[Test]
	public void VisualTreeReaderPassesThroughSnapshot()
	{
		var source = VisualTreeSnapshot.Create(7, new[] { Node("root-0001", isRoot: true) });

		var snapshot = new VisualTreeResponseReader().Read(source);

		Assert.That(snapshot.SequenceNumber, Is.EqualTo(7));
		Assert.That(snapshot.NodeCount, Is.EqualTo(1));
	}

	[Test]
	public void VisualTreeReaderMapsMalformedResponseToProtocolError()
	{
		var ex = Assert.Throws<CliException>(() => new VisualTreeResponseReader().Read(new object()));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.ProtocolError));
	}

	[Test]
	public void VisualTreeReaderRejectsBlankTargetIds()
	{
		Assert.That(
			() => new VisualTreeResponseReader().Read(Snapshot(Node(""))),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.ProtocolError));
	}

	[Test]
	public void VisualTreeReaderDeduplicatesRepeatedTargetIds()
	{
		var snapshot = new VisualTreeResponseReader().Read(Snapshot(
			Node("root", isRoot: true, childIds: new[] { "child", "child" }),
			Node("child", "root"),
			Node("child", "root")));

		Assert.That(snapshot.Nodes.Select(static node => node.TargetId), Is.EqualTo(new[] { "root", "child" }));
		Assert.That(snapshot.Nodes.Single(static node => node.TargetId == "root").ChildIds, Is.EqualTo(new[] { "child" }));
	}

	[Test]
	public void VisualTreeReaderMapsStandardError()
	{
		var response = StandardIpcResponse.FromError("stale", ProtocolConstants.ErrorCodes.StaleTarget);

		var ex = Assert.Throws<CliException>(() => new VisualTreeResponseReader().Read(response));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.StaleTarget));
	}

	[Test]
	public void TargetIdServiceResolvesFullAndShortIds()
	{
		var snapshot = Snapshot(Node("root-aaaa1111", isRoot: true), Node("child-bbbb2222", "root-aaaa1111"));
		var service = new CliTargetIdService();

		Assert.That(service.Resolve("child-bbbb2222", snapshot), Is.EqualTo("child-bbbb2222"));
		Assert.That(service.Resolve("bbbb2222", snapshot), Is.EqualTo("child-bbbb2222"));
	}

	[Test]
	public void TargetIdServiceReportsAmbiguousShortIds()
	{
		var snapshot = Snapshot(Node("left-same", isRoot: true), Node("right-same", isRoot: true));

		var ex = Assert.Throws<CliException>(() => new CliTargetIdService().Resolve("same", snapshot));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.AmbiguousTarget));
	}

	[Test]
	public void TargetIdServiceReportsStaleId()
	{
		var ex = Assert.Throws<CliException>(() => new CliTargetIdService().Resolve("missing", Snapshot(Node("root", isRoot: true))));

		Assert.That(ex!.ErrorCode, Is.EqualTo(CliErrorCodes.StaleTarget));
	}

	[Test]
	public void TreeSnapshotServiceShapesFlatAndNestedOutput()
	{
		var snapshot = Snapshot(Node("root-1", isRoot: true, childIds: new[] { "child-2" }), Node("child-2", "root-1"));
		var service = new TreeSnapshotService();

		var flat = service.Shape(snapshot, new TreeSnapshotOptions { Shape = TreeShape.Flat, IncludePath = true });
		var nested = service.Shape(snapshot, new TreeSnapshotOptions { Shape = TreeShape.Nested });

		Assert.That(flat.Nodes, Has.Count.EqualTo(2));
		Assert.That(flat.Nodes[1].Depth, Is.EqualTo(1));
		Assert.That(flat.Nodes[1].Path, Is.EqualTo("/root-1/child-2"));
		Assert.That(nested.Roots[0].Children, Has.Count.EqualTo(1));
	}

	[Test]
	public void TreeSnapshotServiceDoesNotEmitRepeatedNodesFromDuplicateChildReferences()
	{
		var snapshot = Snapshot(
			Node("root-1", isRoot: true, childIds: new[] { "child-2", "child-2" }),
			Node("child-2", "root-1"));

		var shaped = new TreeSnapshotService().Shape(snapshot, new TreeSnapshotOptions { Shape = TreeShape.Flat });

		Assert.That(shaped.Nodes.Select(static node => node.TargetId), Is.EqualTo(new[] { "root-1", "child-2" }));
	}

	[Test]
	public void TreeSnapshotServiceNormalizesDefaultProperties()
	{
		var snapshot = Snapshot(Node("root", isRoot: true));
		snapshot.Nodes[0].Properties["Complex"] = new Version(1, 2);

		var shaped = new TreeSnapshotService().Shape(snapshot, new TreeSnapshotOptions { Shape = TreeShape.Flat });

		Assert.That(shaped.Nodes[0].Properties["Complex"], Is.EqualTo("1.2"));
	}

	[Test]
	public void TreeSnapshotServiceFiltersHiddenAndMarksLimitTruncation()
	{
		var snapshot = Snapshot(
			Node("root-1", isRoot: true, childIds: new[] { "child-2", "child-3" }),
			Node("child-2", "root-1", visible: false),
			Node("child-3", "root-1"));
		var shaped = new TreeSnapshotService().Shape(snapshot, new TreeSnapshotOptions { Limit = 1, IncludeHidden = false });

		Assert.That(shaped.Nodes, Has.Count.EqualTo(1));
		Assert.That(shaped.Truncated, Is.True);
	}

	[Test]
	public void FindSnapshotServiceMatchesSelectorsAndInvalidRegex()
	{
		var snapshot = Snapshot(Node("root-1", isRoot: true), Node("button-2", "root-1", type: "Button", automationId: "GoButton", text: "Go"));
		var service = new FindSnapshotService();

		var byAutomation = service.Find(snapshot, new FindSnapshotOptions { AutomationId = "GoButton", Limit = 10 });
		var byText = service.Find(snapshot, new FindSnapshotOptions { Text = "go", Limit = 10 });

		Assert.That(byAutomation.MatchCount, Is.EqualTo(1));
		Assert.That(byText.MatchCount, Is.EqualTo(1));
		Assert.That(
			() => service.Find(snapshot, new FindSnapshotOptions { PropertyRegex = new KeyValuePair<string, string>(KnownProperties.Text, "[") }),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.InvalidArguments));
	}

	[Test]
	public void FindSnapshotServiceMatchesFullSelectorMatrix()
	{
		var node = Node("button-2", "root-1", type: "Button", automationId: "GoButton", text: "Go");
		node.Properties[KnownProperties.Content] = "Run";
		node.Properties[KnownProperties.Header] = "Launch";
		node.Properties["Custom"] = "abc-123";
		var snapshot = Snapshot(Node("root-1", isRoot: true, childIds: new[] { "button-2" }), node);
		var service = new FindSnapshotService();

		Assert.That(service.Find(snapshot, new FindSnapshotOptions { TypeName = "Button", Limit = 10 }).MatchCount, Is.EqualTo(1));
		Assert.That(service.Find(snapshot, new FindSnapshotOptions { TypeContains = "but", Limit = 10 }).MatchCount, Is.EqualTo(1));
		Assert.That(service.Find(snapshot, new FindSnapshotOptions { Name = "button-2", Limit = 10 }).MatchCount, Is.EqualTo(1));
		Assert.That(service.Find(snapshot, new FindSnapshotOptions { Text = "Launch", Limit = 10 }).MatchCount, Is.EqualTo(1));
		Assert.That(service.Find(snapshot, new FindSnapshotOptions { PropertyEquals = new KeyValuePair<string, string>("Custom", "abc-123"), Limit = 10 }).MatchCount, Is.EqualTo(1));
		Assert.That(service.Find(snapshot, new FindSnapshotOptions { PropertyContains = new KeyValuePair<string, string>("Custom", "bc-1"), Limit = 10 }).MatchCount, Is.EqualTo(1));
		Assert.That(service.Find(snapshot, new FindSnapshotOptions { PropertyRegex = new KeyValuePair<string, string>("Custom", "abc-\\d+"), Limit = 10 }).MatchCount, Is.EqualTo(1));
		Assert.That(service.Find(snapshot, new FindSnapshotOptions { Visible = true, Enabled = true, Limit = 10 }).MatchCount, Is.EqualTo(2));
	}

	internal static VisualTreeSnapshot Snapshot(params VisualTreeNodeDto[] nodes) => VisualTreeSnapshot.Create(1, nodes);

	internal static VisualTreeNodeDto Node(
		string targetId,
		string? parentId = null,
		bool isRoot = false,
		IReadOnlyList<string>? childIds = null,
		string type = "TextBox",
		string? automationId = null,
		string? text = null,
		bool visible = true)
	{
		var properties = new Dictionary<string, object?>
		{
			[KnownProperties.Name] = targetId,
			[KnownProperties.AutomationName] = targetId,
			[KnownProperties.IsVisible] = visible,
			[KnownProperties.IsEnabled] = true,
		};
		if (automationId is not null)
			properties[KnownProperties.AutomationId] = automationId;
		if (text is not null)
			properties[KnownProperties.Text] = text;

		return new VisualTreeNodeDto
		{
			TargetId = targetId,
			ParentId = parentId,
			IsRoot = isRoot,
			ChildIds = childIds is null ? new List<string>() : new List<string>(childIds),
			TypeName = type,
			FrameworkTypeName = "System.Windows.Controls." + type,
			Properties = properties,
		};
	}
}
