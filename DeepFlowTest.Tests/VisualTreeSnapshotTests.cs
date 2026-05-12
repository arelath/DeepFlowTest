namespace DeepFlowTest.Tests;

using System.Linq;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class VisualTreeSnapshotTests
{
	[Test]
	public void SnapshotRoundTripsThroughMessagePacker()
	{
		var snapshot = VisualTreeSnapshot.Create(
			sequenceNumber: 42,
			nodes: new[]
			{
				new VisualTreeNodeDto
				{
					TargetId = "target-1",
					IsRoot = true,
					Depth = 0,
					TypeName = "Window",
					FrameworkTypeName = "System.Windows.Window",
					ChildIds = { "target-2" },
					Properties =
					{
						["Name"] = "Main",
						["IsVisible"] = true,
					},
				},
				new VisualTreeNodeDto
				{
					TargetId = "target-2",
					ParentId = "target-1",
					Depth = 1,
					SiblingIndex = 0,
					TypeName = "Button",
					Properties =
					{
						["Content"] = "OK",
					},
				},
			},
			requestedPropertyNames: new[] { "Name", "Content", "IsVisible" },
			targetFrameworkFamily: "dotnet");

		var roundTripped = MessagePacker.ConvertTo<VisualTreeSnapshot>(MessagePacker.Unpack(MessagePacker.Pack(snapshot)));

		Assert.That(roundTripped.SequenceNumber, Is.EqualTo(42));
		Assert.That(roundTripped.RootIds, Is.EqualTo(new[] { "target-1" }));
		Assert.That(roundTripped.NodeCount, Is.EqualTo(2));
		Assert.That(roundTripped.RequestedPropertyNames, Is.EqualTo(new[] { "Name", "Content", "IsVisible" }));
		Assert.That(roundTripped.Nodes[0].ChildIds, Is.EqualTo(new[] { "target-2" }));
	}

	[Test]
	public void DeltaDetectsAddedRemovedAndChangedNodes()
	{
		var previous = VisualTreeSnapshot.Create(
			1,
			new[]
			{
				Node("root", null, "Window", ("Title", "Old")),
				Node("removed", "root", "Button", ("Content", "Remove")),
				Node("changed", "root", "TextBox", ("Text", "Before")),
			});
		var current = VisualTreeSnapshot.Create(
			2,
			new[]
			{
				Node("root", null, "Window", ("Title", "Old")),
				Node("changed", "root", "TextBox", ("Text", "After")),
				Node("added", "root", "Button", ("Content", "Add")),
			});

		var delta = VisualTreeSnapshotDelta.Create(previous, current);

		Assert.That(delta.BaseSequenceNumber, Is.EqualTo(1));
		Assert.That(delta.CurrentSequenceNumber, Is.EqualTo(2));
		Assert.That(delta.Added.Select(static node => node.TargetId), Is.EqualTo(new[] { "added" }));
		Assert.That(delta.RemovedTargetIds, Is.EqualTo(new[] { "removed" }));
		Assert.That(delta.Changed.Select(static node => node.TargetId), Is.EqualTo(new[] { "changed" }));
		Assert.That(delta.HasChanges, Is.True);
	}

	[Test]
	public void DeltaComparesPropertiesStructurally()
	{
		var previousNode = Node("target", null, "Button");
		previousNode.Properties["First"] = 1;
		previousNode.Properties["Second"] = "two";
		var currentNode = Node("target", null, "Button");
		currentNode.Properties["Second"] = "two";
		currentNode.Properties["First"] = 1;
		var previous = VisualTreeSnapshot.Create(1, new[] { previousNode });
		var current = VisualTreeSnapshot.Create(2, new[] { currentNode });

		var delta = VisualTreeSnapshotDelta.Create(previous, current);

		Assert.That(delta.Changed, Is.Empty);
		Assert.That(delta.HasChanges, Is.False);
	}

	private static VisualTreeNodeDto Node(string targetId, string? parentId, string typeName, params (string Name, object? Value)[] properties)
	{
		var node = new VisualTreeNodeDto
		{
			TargetId = targetId,
			ParentId = parentId,
			IsRoot = parentId is null,
			Depth = parentId is null ? 0 : 1,
			TypeName = typeName,
		};
		foreach (var property in properties)
			node.Properties[property.Name] = property.Value;

		return node;
	}
}
