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
		var element = driver.GetElement(x => x.TypeName == "Button" && x["Name"] == "Submit", timeout: TimeSpan.FromMilliseconds(1));

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
				Format = ImageFormat.Png,
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
				Format = ImageFormat.Png,
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
				Format = ImageFormat.Jpeg,
				Width = 1,
				Height = 1,
				ByteCount = 2,
				BytesBase64 = Convert.ToBase64String(new byte[] { 9, 10 }),
			},
			new ScreenshotCommandResponse
			{
				TargetId = "button",
				Format = ImageFormat.Jpeg,
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
	public void FluentActionMethodsSendExpectedCommandRequests()
	{
		var sessionResponses = new object[] { FindMatch("button", "submit") }
			.Concat(Enumerable.Range(0, 14).Select(_ => (object)StandardIpcResponse.Ok()))
			.ToArray();
		var session = new FakeSession(sessionResponses);
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("submit"));

		element.RightClick()
			.MiddleClick()
			.Focus()
			.Select()
			.Expand()
			.Collapse()
			.Check()
			.Uncheck()
			.ScrollIntoView()
			.AcceptDialog()
			.CancelDialog()
			.Type("abc", clearFirst: true)
			.RaiseEvent("Click")
			.SetProperty("Text", "updated");

		var commands = session.SentCommands.Skip(1).ToArray();
		Assert.That(commands.Select(static command => command.GetType()), Is.EqualTo(new[]
		{
			typeof(ClickCommandRequest),
			typeof(ClickCommandRequest),
			typeof(FocusCommandRequest),
			typeof(KnownOperationCommandRequest),
			typeof(KnownOperationCommandRequest),
			typeof(KnownOperationCommandRequest),
			typeof(KnownOperationCommandRequest),
			typeof(KnownOperationCommandRequest),
			typeof(KnownOperationCommandRequest),
			typeof(KnownOperationCommandRequest),
			typeof(KnownOperationCommandRequest),
			typeof(TypeTextCommandRequest),
			typeof(RaiseEventCommandRequest),
			typeof(SetPropertyCommandRequest),
		}));

		Assert.That(((ClickCommandRequest)commands[0]).MouseButton, Is.EqualTo(MouseButtonKind.Right));
		Assert.That(((ClickCommandRequest)commands[1]).MouseButton, Is.EqualTo(MouseButtonKind.Middle));
		Assert.That(commands.OfType<KnownOperationCommandRequest>().Select(static command => command.Operation), Is.EqualTo(new[]
		{
			"Select",
			"Expand",
			"Collapse",
			"Check",
			"Uncheck",
			"BringIntoView",
			"AcceptDialog",
			"CancelDialog",
		}));
		var typeText = (TypeTextCommandRequest)commands[11];
		Assert.That(typeText.Text, Is.EqualTo("abc"));
		Assert.That(typeText.ClearFirst, Is.True);
		Assert.That(((RaiseEventCommandRequest)commands[12]).EventName, Is.EqualTo("Click"));
		var setProperty = (SetPropertyCommandRequest)commands[13];
		Assert.That(setProperty.PropertyName, Is.EqualTo("Text"));
		Assert.That(setProperty.PropertyValue, Is.EqualTo("updated"));
		Assert.That(commands.Select(TargetIdOf), Is.All.EqualTo("button"));

		static string? TargetIdOf(IpcCommand command) =>
			command switch
			{
				TargetedIpcCommand targeted => targeted.TargetId,
				TypeTextCommandRequest typeText => typeText.TargetId,
				_ => null,
			};
	}

	[Test]
	public void MouseWheelIsFluentAndSendsSignedDelta()
	{
		var session = new FakeSession(FindMatch("scroller", "items"), StandardIpcResponse.Ok());
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("items"));

		var returned = element.MouseWheel(-240);

		Assert.That(returned, Is.SameAs(element));
		var command = session.SentCommands.OfType<MouseWheelCommandRequest>().Single();
		Assert.That(command.TargetId, Is.EqualTo("scroller"));
		Assert.That(command.Delta, Is.EqualTo(-240));
	}

	[Test]
	public void DragAndDropToSelectorSendsDestinationAndOptions()
	{
		var session = new FakeSession(
			FindMatch("source-target", "drag-source"),
			FindMatch("destination-target", "drop-target"),
			StandardIpcResponse.Ok());
		var driver = CreateDriver(session);
		var source = driver.GetElement(ElementSelector.ByName("drag-source"));
		var options = new DragAndDropOptions
		{
			Duration = TimeSpan.FromMilliseconds(350),
			HoldDuration = TimeSpan.FromMilliseconds(40),
			StepInterval = TimeSpan.FromMilliseconds(7),
			PostDropDelay = TimeSpan.FromMilliseconds(15),
			SourceAnchorX = 0.1,
			SourceAnchorY = 0.2,
			DestinationAnchorX = 0.8,
			DestinationAnchorY = 0.9,
			UseInjectedEvents = true,
			EnsureForeground = false,
			ValidateSameProcess = false,
			Timeout = TimeSpan.FromMilliseconds(1234),
		};

		var returned = source.DragAndDropTo(ElementSelector.ByName("drop-target"), options);

		Assert.That(returned, Is.SameAs(source));
		var findRequests = session.SentCommands.OfType<FindElementCommandRequest>().ToArray();
		Assert.That(findRequests.Select(static request => request.Selector?.Name), Is.EqualTo(new[] { "drag-source", "drop-target" }));
		var drag = session.SentCommands.OfType<DragAndDropCommandRequest>().Single();
		Assert.That(drag.TargetId, Is.EqualTo("source-target"));
		Assert.That(drag.DestinationTargetId, Is.EqualTo("destination-target"));
		Assert.That(drag.DurationMs, Is.EqualTo(350));
		Assert.That(drag.HoldMs, Is.EqualTo(40));
		Assert.That(drag.StepIntervalMs, Is.EqualTo(7));
		Assert.That(drag.PostDropWaitMs, Is.EqualTo(15));
		Assert.That(drag.SourceAnchorX, Is.EqualTo(0.1));
		Assert.That(drag.SourceAnchorY, Is.EqualTo(0.2));
		Assert.That(drag.DestinationAnchorX, Is.EqualTo(0.8));
		Assert.That(drag.DestinationAnchorY, Is.EqualTo(0.9));
		Assert.That(drag.UseInjectedEvents, Is.True);
		Assert.That(drag.EnsureForeground, Is.False);
		Assert.That(drag.ValidateSameProcess, Is.False);
		Assert.That(drag.TimeoutMs, Is.EqualTo(1234));
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

		var exception = Assert.Throws<AssertionException>(() => element.Assert(x => x["Content"] == "Save", timeout: TimeSpan.FromMilliseconds(1)));

		Assert.That(exception!.Message, Does.Contain("Expected:"));
		Assert.That(exception.Message, Does.Contain("Actual:"));
		Assert.That(exception.Message, Does.Contain("Content == \"Cancel\""));
		Assert.That(exception.Message, Does.Contain("TargetId == \"button\""));
	}

	[Test]
	public void DoubleClickUsesMouseDoubleClickRoutedEvent()
	{
		var session = new FakeSession(
			FindMatch("button", "submit"),
			StandardIpcResponse.Ok());
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("submit"));

		var returned = element.DoubleClick();

		Assert.That(returned, Is.SameAs(element));
		var raiseEvent = session.SentCommands.OfType<RaiseEventCommandRequest>().Single();
		Assert.That(raiseEvent.TargetId, Is.EqualTo("button"));
		Assert.That(raiseEvent.EventName, Is.EqualTo("MouseDoubleClick"));
		Assert.That(session.SentCommands.OfType<ClickCommandRequest>(), Is.Empty);
	}

	[Test]
	public void DoubleClickKeepsClickPayloadForWinFormsAndNativeTargets()
	{
		var session = new FakeSession(
			new FindElementCommandResponse
			{
				Matches =
				{
					new FindElementMatchResponse
					{
						TargetId = "forms-button",
						TypeName = "Button",
						FrameworkTypeName = "System.Windows.Forms.Button",
						Properties = { ["Name"] = "submit" },
					},
				},
				MatchCount = 1,
			},
			StandardIpcResponse.Ok());
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("submit"));

		element.DoubleClick();

		var click = session.SentCommands.OfType<ClickCommandRequest>().Single();
		Assert.That(click.TargetId, Is.EqualTo("forms-button"));
		Assert.That(click.ClickCount, Is.EqualTo(2));
		Assert.That(session.SentCommands.OfType<RaiseEventCommandRequest>(), Is.Empty);
	}

	[Test]
	public void ElementScreenshotWaitsForAdjacentStableCapture()
	{
		var first = Convert.ToBase64String(new byte[] { 1 });
		var stable = Convert.ToBase64String(new byte[] { 2 });
		var session = new FakeSession(
			FindMatch("image-target", "image"),
			new ScreenshotCommandResponse { TargetId = "image-target", Format = ImageFormat.Png, ByteCount = 1, BytesBase64 = first },
			new ScreenshotCommandResponse { TargetId = "image-target", Format = ImageFormat.Png, ByteCount = 1, BytesBase64 = stable },
			new ScreenshotCommandResponse { TargetId = "image-target", Format = ImageFormat.Png, ByteCount = 1, BytesBase64 = stable });
		var driver = CreateDriver(session);
		var element = driver.GetElement(ElementSelector.ByName("image"));

		var bytes = element.Screenshot(ImageFormat.Png);

		Assert.That(bytes, Is.EqualTo(new byte[] { 2 }));
		Assert.That(session.SentCommands.OfType<ScreenshotCommandRequest>().Count(), Is.EqualTo(3));
	}

	private static AppDriver CreateDriver(IUnsafeAppDriverCommandSession session)
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
