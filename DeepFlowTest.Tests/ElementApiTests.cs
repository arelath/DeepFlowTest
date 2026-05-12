namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class ElementApiTests
{
	[Test]
	public void TraversalExposesParentsChildrenDescendantsAndIndexer()
	{
		var snapshot = VisualTreeSnapshot.Create(
			1,
			new[]
			{
				Node("root", null, "Window", "Root"),
				Node("child", "root", "StackPanel", "Child"),
				Node("grandchild", "child", "Button", "Grandchild"),
			});
		snapshot.Nodes[0].ChildIds.Add("child");
		snapshot.Nodes[1].ChildIds.Add("grandchild");
		var driver = CreateDriver(new FakeSession(snapshot));

		var root = driver.GetRootElements().Single();
		var child = root[0];
		var grandchild = root.Descendants.Single(element => element.TargetId == "grandchild");

		Assert.That(root.TypeName, Is.EqualTo("Window"));
		Assert.That(child.Parent!.TargetId, Is.EqualTo("root"));
		Assert.That(grandchild.Parent!.TargetId, Is.EqualTo("child"));
		Assert.That(root.Children.Select(static element => element.TargetId), Is.EqualTo(new[] { "child" }));
	}

	[Test]
	public void PropertiesAreAvailableWithTypedConversion()
	{
		var session = new FakeSession(new FindElementCommandResponse
		{
			Matches =
			{
				new FindElementMatchResponse
				{
					TargetId = "button",
					TypeName = "Button",
					Properties =
					{
						["Name"] = "submit",
						["Count"] = "42",
					},
				},
			},
			MatchCount = 1,
		});
		var driver = CreateDriver(session);

		var element = driver.GetElement(ElementSelector.ByName("submit"));

		Assert.That(element.TargetId, Is.EqualTo("button"));
		Assert.That(element.GetProperty<string>("Name"), Is.EqualTo("submit"));
		Assert.That(element.GetProperty<int>("Count"), Is.EqualTo(42));
	}

	[Test]
	public void ChildrenRefreshNodeFromCurrentSnapshotForFindResults()
	{
		var snapshot = VisualTreeSnapshot.Create(
			1,
			new[]
			{
				Node("parent", null, "StackPanel", "Parent"),
				Node("child", "parent", "Button", "Child"),
			});
		snapshot.Nodes[0].ChildIds.Add("child");
		var session = new FakeSession(FindMatch("parent", "parent"), snapshot);
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("parent"));

		var child = element.Children.Single();

		Assert.That(child.TargetId, Is.EqualTo("child"));
	}

	[Test]
	public void StaleElementRepairsByRerunningOriginalSelector()
	{
		var session = new FakeSession(
			FindMatch("old-target", "submit"),
			StandardIpcResponse.FromError("stale", ProtocolConstants.ErrorCodes.StaleTarget),
			FindMatch("new-target", "submit"),
			StandardIpcResponse.Ok());
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("submit"));

		element.Click();

		var clicks = session.SentCommands.OfType<ClickCommandRequest>().ToArray();
		Assert.That(clicks.Select(static command => command.TargetId), Is.EqualTo(new[] { "old-target", "new-target" }));
		Assert.That(element.TargetId, Is.EqualTo("new-target"));
	}

	[Test]
	public void TypedStaleScreenshotRepairsBeforeReturning()
	{
		var session = new FakeSession(
			FindMatch("old-target", "image"),
			new ScreenshotCommandResponse
			{
				Success = false,
				Status = ProtocolConstants.Statuses.Error,
				ErrorCode = ProtocolConstants.ErrorCodes.StaleTarget,
				Error = "stale",
			},
			FindMatch("new-target", "image"),
			new ScreenshotCommandResponse
			{
				TargetId = "new-target",
				Format = "png",
				Width = 10,
				Height = 20,
				ByteCount = 3,
				BytesBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
			});
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("image"));

		var screenshot = element.Screenshot();

		Assert.That(screenshot.TargetId, Is.EqualTo("new-target"));
		Assert.That(element.TargetId, Is.EqualTo("new-target"));
	}

	[Test]
	public void AmbiguousStaleRepairFailsClearly()
	{
		var ambiguous = new FindElementCommandResponse
		{
			Matches =
			{
				new FindElementMatchResponse { TargetId = "new-1", TypeName = "Button" },
				new FindElementMatchResponse { TargetId = "new-2", TypeName = "Button" },
			},
			MatchCount = 2,
		};
		var session = new FakeSession(
			FindMatch("old-target", "submit"),
			StandardIpcResponse.FromError("stale", ProtocolConstants.ErrorCodes.StaleTarget),
			ambiguous);
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("submit"));

		var exception = Assert.Throws<AppDriverException>(() => element.Click());

		Assert.That(exception!.ErrorCode, Is.EqualTo(AppDriverErrorCodes.AmbiguousTarget));
	}

	[Test]
	public void ScreenshotUsesElementTarget()
	{
		var session = new FakeSession(
			FindMatch("image-target", "image"),
			new ScreenshotCommandResponse
			{
				TargetId = "image-target",
				Format = "png",
				Width = 10,
				Height = 20,
				ByteCount = 3,
				BytesBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
			});
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("image"));

		var screenshot = element.Screenshot();

		Assert.That(screenshot.TargetId, Is.EqualTo("image-target"));
		Assert.That(session.SentCommands.OfType<ScreenshotCommandRequest>().Single().TargetId, Is.EqualTo("image-target"));
	}

	private static AppDriver CreateDriver(IAppDriverCommandSession session)
	{
		return AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session);
	}

	private static FindElementCommandResponse FindMatch(string targetId, string name)
	{
		return new FindElementCommandResponse
		{
			Matches =
			{
				new FindElementMatchResponse
				{
					TargetId = targetId,
					TypeName = "Button",
					Properties = { ["Name"] = name },
				},
			},
			MatchCount = 1,
		};
	}

	private static VisualTreeNodeDto Node(string targetId, string? parentId, string typeName, string name)
	{
		return new VisualTreeNodeDto
		{
			TargetId = targetId,
			ParentId = parentId,
			TypeName = typeName,
			Properties = { ["Name"] = name },
		};
	}

	private sealed class FakeSession : IAppDriverCommandSession
	{
		private readonly Queue<object> responses;

		public FakeSession(params object[] responses)
		{
			this.responses = new Queue<object>(responses);
		}

		public List<IpcCommand> SentCommands { get; } = new();

		public TResponse Send<TResponse>(IpcCommand command)
		{
			SentCommands.Add(command);
			return (TResponse)responses.Dequeue();
		}
	}

	private sealed class FakeTargetProcess : ITargetProcess
	{
		public int Id => 123;

		public string ProcessName => "target";

		public bool HasExited { get; private set; }

		public void Kill()
		{
			HasExited = true;
		}

		public void Dispose()
		{
		}
	}
}
