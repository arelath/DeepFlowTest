namespace DeepFlowTest.Tests;

using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class ElementContextTests
{
	[Test]
	public void SnapshotBackedElementCanBeWrappedWithoutAcquiringClientState()
	{
		var node = new VisualTreeNodeDto { TargetId = "snapshot", TypeName = "Button" };
		var snapshot = VisualTreeSnapshot.Create(1, [node]);
		var element = Element.FromSnapshot(node, snapshot);

		var wrapped = new SnapshotElementWrapper(element);

		Assert.That(wrapped.TargetId, Is.EqualTo("snapshot"));
		Assert.That(() => wrapped.Click(), Throws.TypeOf<System.InvalidOperationException>());
	}

	private sealed class SnapshotElementWrapper : Element
	{
		public SnapshotElementWrapper(Element source)
			: base(source)
		{
		}
	}
}
