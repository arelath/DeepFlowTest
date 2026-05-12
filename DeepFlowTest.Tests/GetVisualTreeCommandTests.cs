namespace DeepFlowTest.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class GetVisualTreeCommandTests
{
	[Test]
	public void SnapshotResponseReturnsRootsAndParentChildLinks()
	{
		var panel = new StackPanel { Name = "rootPanel" };
		panel.Children.Add(new Button { Name = "childButton", Content = "Child" });
		var window = CreateWindow("Snapshot", panel);

		try
		{
			window.Show();

			var snapshot = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = ["Name", "Content", "Title"],
				MaxNodeCount = 200,
			})!;

			var panelNode = FindByName(snapshot, "rootPanel");
			var buttonNode = FindByName(snapshot, "childButton");

			Assert.That(snapshot.RootIds, Is.Not.Empty);
			Assert.That(panelNode, Is.Not.Null);
			Assert.That(buttonNode, Is.Not.Null);
			Assert.That(buttonNode!.ParentId, Is.EqualTo(panelNode!.TargetId));
			Assert.That(panelNode.ChildIds, Does.Contain(buttonNode.TargetId));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void LegacyResponseReturnsNodeList()
	{
		var window = CreateWindow("Legacy", new Button { Name = "legacyButton", Content = "Legacy" });

		try
		{
			window.Show();

			var response = CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = false,
				PropNames = ["Name", "Content"],
				MaxNodeCount = 200,
			});

			Assert.That(response, Is.TypeOf<System.Collections.Generic.List<VisualTreeNodeDto>>());
			Assert.That(((System.Collections.Generic.List<VisualTreeNodeDto>)response!).Any(node =>
				node.Properties.TryGetValue("Name", out var name) && Equals(name, "legacyButton")), Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void RootTargetLimitsTraversal()
	{
		var panel = new StackPanel { Name = "limitedRoot" };
		panel.Children.Add(new Button { Name = "limitedChild", Content = "Child" });
		var window = CreateWindow("Root target", panel);

		try
		{
			window.Show();
			var fullSnapshot = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = ["Name", "Content", "Title"],
				MaxNodeCount = 200,
			})!;
			var panelNode = FindByName(fullSnapshot, "limitedRoot")!;

			var limitedSnapshot = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				RootTargetId = panelNode.TargetId,
				PropNames = ["Name", "Content", "Title"],
				MaxNodeCount = 200,
			})!;

			Assert.That(limitedSnapshot.RootIds, Is.EqualTo(new[] { panelNode.TargetId }));
			Assert.That(limitedSnapshot.Nodes.Any(static node => node.TypeName == "Window"), Is.False);
			Assert.That(FindByName(limitedSnapshot, "limitedChild"), Is.Not.Null);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void LimitAndMaxDepthTruncatePredictably()
	{
		var panel = new StackPanel { Name = "depthRoot" };
		panel.Children.Add(new Button { Name = "depthChild", Content = "Child" });
		var window = CreateWindow("Truncation", panel);

		try
		{
			window.Show();

			var depthLimited = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = ["Name"],
				MaxDepth = 0,
				MaxNodeCount = 200,
			})!;
			var countLimited = (VisualTreeSnapshot)CaptureResponse(new GetVisualTreeCommandRequest
			{
				AsSnapshot = true,
				PropNames = ["Name"],
				MaxNodeCount = 1,
			})!;

			Assert.That(depthLimited.IsTruncated, Is.True);
			Assert.That(depthLimited.Nodes.All(static node => node.Depth == 0), Is.True);
			Assert.That(depthLimited.TruncationReason, Does.Contain("max depth"));

			Assert.That(countLimited.IsTruncated, Is.True);
			Assert.That(countLimited.NodeCount, Is.EqualTo(1));
			Assert.That(countLimited.TruncationReason, Does.Contain("node limit"));
		}
		finally
		{
			window.Close();
		}
	}

	private static object? CaptureResponse(object request)
	{
		PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");
		object? response = null;
		var responseCount = 0;
		var command = new NamedPipeServer.Command
		{
			Value = request,
			Respond = value =>
			{
				response = value;
				responseCount++;
			},
			CheckHasResponded = () => responseCount != 0,
			HoldConnectionOpen = () => { },
			TrySend = value =>
			{
				response = value;
				responseCount++;
				return true;
			},
		};
		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = "test-pipe",
			Mode = PayloadStartupModes.OneShotDriver,
			PayloadRoot = AppContext.BaseDirectory,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};

		AppDriverCommandDispatcher.Process(command, options, null);
		return response;
	}

	private static Window CreateWindow(string title, object content)
	{
		return new Window
		{
			Title = title,
			Content = content,
			Width = 240,
			Height = 160,
			ShowInTaskbar = false,
			WindowStartupLocation = WindowStartupLocation.Manual,
			Left = -20000,
			Top = -20000,
		};
	}

	private static VisualTreeNodeDto? FindByName(VisualTreeSnapshot snapshot, string name)
	{
		return snapshot.Nodes.SingleOrDefault(node =>
			node.Properties.TryGetValue("Name", out var value) && Equals(value, name));
	}
}
