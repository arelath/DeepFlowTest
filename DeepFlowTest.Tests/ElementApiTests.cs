namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Tests.Fakes;
using NUnit.Framework;
using FakeSession = DeepFlowTest.Tests.Fakes.FakeAppDriverCommandSession;

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
		Assert.That(root.Child.Select(static element => element.TargetId), Is.EqualTo(new[] { "child" }));
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
	public void CachedElementsRefreshWhenVisualTreeSnapshotsUpdate()
	{
		var first = VisualTreeSnapshot.Create(1, new[] { Node("root", null, "Window", "Before") });
		var second = VisualTreeSnapshot.Create(2, new[] { Node("root", null, "Window", "After") });
		var driver = CreateDriver(new FakeSession(first, second));
		var root = driver.GetRootElements().Single();

		driver.GetVisualTree();

		Assert.That(root.GetProperty<string>("Name"), Is.EqualTo("After"));
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
	public void StaleExpressionElementRepairsUsingMatcherAndIdentityProperties()
	{
		var second = VisualTreeSnapshot.Create(2, new[]
		{
			Node(
				"new-target",
				null,
				"Button",
				"Submit",
				new Dictionary<string, object?>
				{
					["AutomationProperties.AutomationId"] = "SubmitButton",
					["ActualWidth"] = 120,
					["ActualHeight"] = 30,
				}),
			Node(
				"distractor",
				null,
				"Button",
				"Submit",
				new Dictionary<string, object?>
				{
					["AutomationProperties.AutomationId"] = "OtherButton",
					["ActualWidth"] = 120,
					["ActualHeight"] = 30,
				}),
		});
		var session = new FakeSession(
			new FindElementCommandResponse
			{
				Status = ProtocolConstants.Statuses.Ok,
				Matches =
				{
					new FindElementMatchResponse
					{
						TargetId = "old-target",
						TypeName = "Button",
						Properties =
						{
							["Name"] = "Submit",
							["AutomationProperties.AutomationId"] = "SubmitButton",
						},
					},
				},
				MatchCount = 1,
			},
			StandardIpcResponse.FromError("stale", ProtocolConstants.ErrorCodes.StaleTarget),
			second,
			StandardIpcResponse.Ok());
		var driver = CreateDriver(session);
		var element = driver.GetElement(x => x.TypeName == "Button" && x["Name"] == "Submit", timeoutMs: 1);

		element.Click();

		var clicks = session.SentCommands.OfType<ClickCommandRequest>().ToArray();
		Assert.That(clicks.Select(static command => command.TargetId), Is.EqualTo(new[] { "old-target", "new-target" }));
		Assert.That(element.TargetId, Is.EqualTo("new-target"));
		Assert.That(session.SentCommands.OfType<GetVisualTreeCommandRequest>().Last().PropNames, Does.Contain("Name"));
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

		var screenshot = element.CaptureScreenshot();

		Assert.That(screenshot.TargetId, Is.EqualTo("new-target"));
		Assert.That(element.TargetId, Is.EqualTo("new-target"));
	}

	[Test]
	public void TypedInvokeThrowsSerializationExceptionForUnserializableResultStatus()
	{
		var session = new FakeSession(
			FindMatch("target", "invoke"),
			StandardIpcResponse.UnserializableResult());
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("invoke"));

		var exception = Assert.Throws<System.Runtime.Serialization.SerializationException>(
			() => element.Invoke<object, object>(x => x));

		Assert.That(exception!.Message, Does.Contain("Unserializable Invoke result"));
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

		var screenshot = element.CaptureScreenshot();

		Assert.That(screenshot.TargetId, Is.EqualTo("image-target"));
		Assert.That(session.SentCommands.OfType<ScreenshotCommandRequest>().Single().TargetId, Is.EqualTo("image-target"));
	}

	[Test]
	public void CompatibilityPrimitiveIndexerFluentActionsAndScreenshotBytesWork()
	{
		var session = new FakeSession(
			FindMatch("button", "submit"),
			StandardIpcResponse.Ok(),
			new ScreenshotCommandResponse
			{
				TargetId = "button",
				Format = "jpeg",
				Width = 1,
				Height = 1,
				ByteCount = 2,
				BytesBase64 = Convert.ToBase64String(new byte[] { 9, 10 }),
			},
			new ScreenshotCommandResponse
			{
				TargetId = "button",
				Format = "jpeg",
				Width = 1,
				Height = 1,
				ByteCount = 2,
				BytesBase64 = Convert.ToBase64String(new byte[] { 9, 10 }),
			});
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("submit"));

		var chained = element.Click();
		var bytes = element.Screenshot(ImageFormat.Jpeg);

		Assert.That(chained, Is.SameAs(element));
		Assert.That(element.HasProperty("Name"), Is.True);
		Assert.That(element["Name"] == "submit", Is.True);
		Assert.That(bytes, Is.EqualTo(new byte[] { 9, 10 }));
	}

	[Test]
	public void ElementAssertUsesDiagnosticAssertionFramework()
	{
		var match = new FindElementCommandResponse
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
						["Content"] = "Cancel",
					},
				},
			},
			MatchCount = 1,
		};
		var refreshed = VisualTreeSnapshot.Create(1, new[]
		{
			new VisualTreeNodeDto
			{
				TargetId = "button",
				TypeName = "Button",
				Properties =
				{
					["Name"] = "submit",
					["Content"] = "Cancel",
				},
			},
		});
		var driver = CreateDriver(new FakeSession(match, refreshed, refreshed, refreshed));
		var element = driver.GetElement(ElementSelector.ByName("submit"));

		var exception = Assert.Throws<AssertionException>(() => element.Assert(x => x["Content"] == "Save", timeoutMs: 1));

		Assert.That(exception!.Message, Does.Contain("Expected:"));
		Assert.That(exception.Message, Does.Contain("Actual:"));
		Assert.That(exception.Message, Does.Contain("Content == \"Cancel\""));
		Assert.That(exception.Message, Does.Contain("TargetId == \"button\""));
	}

	[Test]
	public void ElementScreenshotWaitsForAdjacentStableCapture()
	{
		var first = Convert.ToBase64String(new byte[] { 1 });
		var stable = Convert.ToBase64String(new byte[] { 2 });
		var session = new FakeSession(
			FindMatch("image-target", "image"),
			new ScreenshotCommandResponse { TargetId = "image-target", Format = "png", ByteCount = 1, BytesBase64 = first },
			new ScreenshotCommandResponse { TargetId = "image-target", Format = "png", ByteCount = 1, BytesBase64 = stable },
			new ScreenshotCommandResponse { TargetId = "image-target", Format = "png", ByteCount = 1, BytesBase64 = stable });
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("image"));

		var bytes = element.Screenshot(ImageFormat.Png);

		Assert.That(bytes, Is.EqualTo(new byte[] { 2 }));
		Assert.That(session.SentCommands.OfType<ScreenshotCommandRequest>().Count(), Is.EqualTo(3));
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

	private static VisualTreeNodeDto Node(
		string targetId,
		string? parentId,
		string typeName,
		string name,
		Dictionary<string, object?>? extraProperties = null)
	{
		var node = new VisualTreeNodeDto
		{
			TargetId = targetId,
			ParentId = parentId,
			TypeName = typeName,
			Properties = { ["Name"] = name },
		};
		if (extraProperties is not null)
			foreach (var property in extraProperties)
				node.Properties[property.Key] = property.Value;

		return node;
	}

}
