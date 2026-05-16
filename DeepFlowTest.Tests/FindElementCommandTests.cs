namespace DeepFlowTest.Tests;

using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;
using static DeepFlowTest.Tests.TestIpcHost;
using static DeepFlowTest.Tests.WpfTestHelpers;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class FindElementCommandTests
{
	[Test]
	public void StructuredSelectorsFindCommonElementProperties()
	{
		var window = CreateSelectorWindow();

		try
		{
			window.Show();

			AssertSingleMatch(new ElementSelectorDto { TypeName = "Button", Content = "Save" }, "saveButton");
			AssertSingleMatch(new ElementSelectorDto { Name = "saveButton" }, "saveButton");
			AssertSingleMatch(new ElementSelectorDto { AutomationId = "input-id" }, "inputBox");
			AssertSingleMatch(new ElementSelectorDto { Text = "Ready" }, "statusText");
			AssertSingleMatch(new ElementSelectorDto { Properties = { ["Content"] = "Save" } }, "saveButton");
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void MaxMatchesAndNoMatchAreNormalResults()
	{
		var panel = new StackPanel();
		panel.Children.Add(new Button { Name = "first", Content = "Duplicate" });
		panel.Children.Add(new Button { Name = "second", Content = "Duplicate" });
		var window = CreateWindow("Find max", panel);

		try
		{
			window.Show();

			var limited = Find(new ElementSelectorDto { TypeName = "Button" }, maxMatches: 1);
			var noMatch = Find(new ElementSelectorDto { Name = "missing" }, maxMatches: 3);

			Assert.That(limited.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(limited.MatchCount, Is.EqualTo(1));
			Assert.That(limited.MaxMatches, Is.EqualTo(1));

			Assert.That(noMatch.Success, Is.True);
			Assert.That(noMatch.Status, Is.EqualTo(ProtocolConstants.Statuses.NoMatch));
			Assert.That(noMatch.MatchCount, Is.EqualTo(0));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void MaxMatchesZeroReturnsAllMatches()
	{
		var panel = new StackPanel();
		panel.Children.Add(new Button { Name = "first", Content = "Duplicate" });
		panel.Children.Add(new Button { Name = "second", Content = "Duplicate" });
		var window = CreateWindow("Find all", panel);

		try
		{
			window.Show();

			var allMatches = Find(new ElementSelectorDto { TypeName = "Button" }, maxMatches: 0);

			Assert.That(allMatches.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(allMatches.MatchCount, Is.EqualTo(2));
			Assert.That(allMatches.MaxMatches, Is.EqualTo(0));
			Assert.That(allMatches.Matches.Select(match => match.Properties["Name"]), Is.EqualTo(new[] { "first", "second" }));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ExpressionMatcherFindsOnTargetSide()
	{
		var window = CreateWindow("Expression", new Button { Name = "expressionButton", Content = "Expression" });

		try
		{
			window.Show();
			var expectedType = "Button";
			Expression<Func<VisualTreeNodeDto, bool>> matcher = node => node.TypeName == expectedType;
			var payload = ExpressionPayloadSerializer.Serialize(matcher);

			var response = (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
			{
				MatcherCode = payload,
				MatcherHash = payload.ExpressionHash,
				PropNames = ["Name", "Content"],
				MaxMatches = 1,
			})!;

			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(response.MatchCount, Is.EqualTo(1));
			Assert.That(response.Matches[0].TypeName, Is.EqualTo("Button"));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void DefaultServerFindScansBeyondLegacyThousandNodeLimit()
	{
		var panel = new StackPanel();
		for (var i = 0; i < 1100; i++)
			panel.Children.Add(new Button { Name = $"fillerButton{i}", Content = $"Filler {i}" });
		panel.Children.Add(new Button { Name = "lateButton", Content = "Late" });
		var window = CreateWindow("Large find", panel);

		try
		{
			window.Show();

			var response = (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
			{
				Selector = new ElementSelectorDto { Name = "lateButton" },
				PropNames = ["Name", "Content"],
				MaxMatches = 1,
			})!;

			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(response.MatchCount, Is.EqualTo(1));
			Assert.That(response.Matches[0].Properties["Name"], Is.EqualTo("lateButton"));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void FindElementLogsWarningWhenSnapshotHitsMaxNodeCount()
	{
		var panel = new StackPanel();
		for (var i = 0; i < 8; i++)
			panel.Children.Add(new Button { Name = $"button{i}", Content = $"Button {i}" });
		var window = CreateWindow("Truncated find", panel);

		try
		{
			window.Show();
			var logPath = PayloadLog.Initialize($"deepflowtest-truncated-find-{Guid.NewGuid():N}");
			try
			{
				_ = InvokeFindElementProcess(new FindElementCommandRequest
				{
					Selector = new ElementSelectorDto { Name = "missing" },
					PropNames = ["Name"],
					MaxNodeCount = 3,
					MaxMatches = 1,
				}, new DeepFlowTest.Utility.WpfUtility.Tree.TreeService(), new ExpressionCache());

				var log = PayloadLog.ReadTail(logPath, maxCharacters: 4096);

				Assert.That(log, Does.Contain("FindElementCommand warning"));
				Assert.That(log, Does.Contain("MaxNodeCount=3"));
				Assert.That(log, Does.Contain("Snapshot node limit 3 was reached."));
			}
			finally
			{
				File.Delete(logPath);
			}
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ElementExpressionMatcherFindsOnTargetSide()
	{
		var panel = new StackPanel { Name = "parentPanel" };
		panel.Children.Add(new Button { Name = "expressionButton", Content = "Expression" });
		var window = CreateWindow("Element expression", panel);

		try
		{
			window.Show();
			var expectedName = "expressionButton";
			Expression<Func<DeepFlowTest.Element, bool?>> matcher = element =>
				element.TypeName == "Button"
				&& element["Name"] == expectedName
				&& element.Parent!.TypeName == "StackPanel"
				&& element.Parent["Name"] == "parentPanel";

			var response = (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
			{
				MatcherCode = Eval.SerializeCode(matcher),
				PropNames = ["Name", "Content"],
				MaxMatches = 1,
			})!;

			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(response.MatchCount, Is.EqualTo(1));
			Assert.That(response.Matches[0].Properties["Name"], Is.EqualTo(expectedName));
			Assert.That(response.Matches[0].Path.Select(static segment => segment.TypeName), Does.Contain("StackPanel"));
			Assert.That(response.Matches[0].Path.Select(static segment => segment.TypeName), Does.Contain("Button"));
			Assert.That(response.Matches[0].Path.Any(static segment =>
				segment.TypeName == "StackPanel"
				&& segment.Properties.TryGetValue("Name", out var name)
				&& Equals(name, "parentPanel")), Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void RootTargetIdScopesFindToDescendantsOnTargetSide()
	{
		var rootPanel = new StackPanel { Name = "rootPanel" };
		rootPanel.Children.Add(new Button { Name = "childOne", Content = "Child" });
		rootPanel.Children.Add(new Button { Name = "childTwo", Content = "Child" });
		var siblingPanel = new StackPanel { Name = "siblingPanel" };
		siblingPanel.Children.Add(new Button { Name = "outsideChild", Content = "Child" });
		var container = new StackPanel();
		container.Children.Add(rootPanel);
		container.Children.Add(siblingPanel);
		var window = CreateWindow("Root find", container);

		try
		{
			window.Show();
			var treeService = new DeepFlowTest.Utility.WpfUtility.Tree.TreeService();
			var cache = new ExpressionCache();
			var rootResponse = (FindElementCommandResponse)InvokeFindElementProcess(new FindElementCommandRequest
			{
				Selector = new ElementSelectorDto { Name = "rootPanel" },
				PropNames = ["Name"],
				MaxMatches = 1,
			}, treeService, cache)!;
			var rootTargetId = rootResponse.Matches.Single().TargetId;

			var response = (FindElementCommandResponse)InvokeFindElementProcess(new FindElementCommandRequest
			{
				RootTargetId = rootTargetId,
				IncludeRoot = false,
				Selector = new ElementSelectorDto { TypeName = "Button", Content = "Child" },
				PropNames = ["Name", "Content"],
				MaxMatches = 0,
			}, treeService, cache)!;

			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(response.Matches.Select(static match => match.Properties["Name"]), Is.EqualTo(new[] { "childOne", "childTwo" }));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void RootMatcherScopesFindToDescendantsOnTargetSide()
	{
		var rootPanel = new StackPanel { Name = "rootPanel" };
		rootPanel.Children.Add(new Button { Name = "childOne", Content = "Child" });
		rootPanel.Children.Add(new Button { Name = "childTwo", Content = "Child" });
		var siblingPanel = new StackPanel { Name = "siblingPanel" };
		siblingPanel.Children.Add(new Button { Name = "outsideChild", Content = "Child" });
		var container = new StackPanel();
		container.Children.Add(rootPanel);
		container.Children.Add(siblingPanel);
		var window = CreateWindow("Root matcher find", container);

		try
		{
			window.Show();
			var treeService = new DeepFlowTest.Utility.WpfUtility.Tree.TreeService();
			var cache = new ExpressionCache();
			Expression<Func<DeepFlowTest.Element, bool?>> rootMatcher = element =>
				element.TypeName == "StackPanel" && element["Name"] == "rootPanel";
			Expression<Func<DeepFlowTest.Element, bool?>> matcher = element =>
				element.TypeName == "Button" && element["Content"] == "Child";

			var response = (FindElementCommandResponse)InvokeFindElementProcess(new FindElementCommandRequest
			{
				RootMatcherCode = Eval.SerializeCode(rootMatcher),
				RootMatcherHash = ExpressionPayloadSerializer.Serialize(rootMatcher).ExpressionHash,
				IncludeRoot = false,
				MatcherCode = Eval.SerializeCode(matcher),
				MatcherHash = ExpressionPayloadSerializer.Serialize(matcher).ExpressionHash,
				PropNames = ["Name", "Content"],
				MaxMatches = 0,
			}, treeService, cache)!;

			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
			Assert.That(response.Matches.Select(static match => match.Properties["Name"]), Is.EqualTo(new[] { "childOne", "childTwo" }));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void RepeatedExpressionMatcherUsesCommandCache()
	{
		var window = CreateWindow("Expression cache", new Button { Name = "cachedButton", Content = "Cached" });

		try
		{
			window.Show();
			Expression<Func<VisualTreeNodeDto, bool>> matcher = node => node.TypeName == "Button";
			var payload = ExpressionPayloadSerializer.Serialize(matcher);
			var request = new FindElementCommandRequest
			{
				MatcherCode = payload,
				MatcherHash = payload.ExpressionHash,
				PropNames = ["Name", "Content"],
				MaxMatches = 1,
			};
			var cache = new ExpressionCache();
			var treeService = new DeepFlowTest.Utility.WpfUtility.Tree.TreeService();

			_ = InvokeFindElementProcess(request, treeService, cache);
			_ = InvokeFindElementProcess(request, treeService, cache);

			Assert.That(cache.Stats.CompileCount, Is.EqualTo(1));
			Assert.That(cache.Stats.HitCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	private static void AssertSingleMatch(ElementSelectorDto selector, string expectedName)
	{
		var response = Find(selector, maxMatches: 1);

		Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
		Assert.That(response.MatchCount, Is.EqualTo(1));
		Assert.That(response.Matches[0].Properties["Name"], Is.EqualTo(expectedName));
	}

	private static FindElementCommandResponse Find(ElementSelectorDto selector, int maxMatches)
	{
		return (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
		{
			Selector = selector,
			PropNames = ["Name", "Text", "Content", "AutomationProperties.AutomationId"],
			MaxMatches = maxMatches,
		})!;
	}

	private static Window CreateSelectorWindow()
	{
		var panel = new StackPanel();
		var saveButton = new Button { Name = "saveButton", Content = "Save" };
		var input = new TextBox { Name = "inputBox", Text = "Typed" };
		AutomationProperties.SetAutomationId(input, "input-id");
		panel.Children.Add(saveButton);
		panel.Children.Add(input);
		panel.Children.Add(new TextBlock { Name = "statusText", Text = "Ready" });
		return CreateWindow("Find selectors", panel);
	}

	private static object? InvokeFindElementProcess(FindElementCommandRequest request, DeepFlowTest.Utility.WpfUtility.Tree.TreeService treeService, ExpressionCache cache)
	{
		return FindElementCommand.Process(request, treeService, cache);
	}
}
