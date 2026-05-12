namespace DeepFlowTest.Tests;

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

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
				PropNames = new[] { "Name", "Content" },
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
				PropNames = new[] { "Name", "Content" },
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
			PropNames = new[] { "Name", "Text", "Content", "AutomationProperties.AutomationId" },
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

		var dispatcherType = Type.GetType("DeepFlowTest.AppDriverPayload.AppDriverCommandDispatcher, DeepFlowTest", throwOnError: true)!;
		var method = dispatcherType.GetMethod("Process", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
		method.Invoke(null, new object?[] { command, options, null });
		return response;
	}

	private static object? InvokeFindElementProcess(FindElementCommandRequest request, DeepFlowTest.Utility.WpfUtility.Tree.TreeService treeService, ExpressionCache cache)
	{
		var commandType = Type.GetType("DeepFlowTest.AppDriverPayload.Commands.FindElementCommand, DeepFlowTest", throwOnError: true)!;
		var method = commandType.GetMethod("Process", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
		return method.Invoke(null, new object[] { request, treeService, cache });
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
}
