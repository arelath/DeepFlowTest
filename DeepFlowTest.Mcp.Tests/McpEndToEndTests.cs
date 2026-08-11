namespace DeepFlowTest.Mcp.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

[TestFixture]
[Category("Integration")]
[Explicit("Starts the MCP server, launches or attaches to a real HelloWorld desktop harness, and injects into it.")]
[NonParallelizable]
public sealed class McpEndToEndTests
{
	private const string DefaultProperties =
		"AutomationProperties.AutomationId,Name,Text,Content,Header,IsVisible,IsEnabled,IsFocused,IsKeyboardFocused,IsKeyboardFocusWithin,IsChecked,IsExpanded,IsSubmenuOpen";

	[Test]
	public async Task AgentProfileUsesExplicitContextStructuredContractsAndNativeContent()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync(
			"--tool-profile", "agent", "--allow-launch", "--allow-actions",
			"--timeout-ms", "60000", "--attach-timeout-ms", "60000");
		string? contextId = null;
		try
		{
			var opened = await mcp.CallRawAsync("deepflow_open_context", new Dictionary<string, object?>
			{
				["target"] = new Dictionary<string, object?>
				{
					["mode"] = "launch",
					["fileName"] = McpEndToEndHarness.ResolveHelloWorldExecutablePath(),
					["terminateOnClose"] = true,
				},
				["timeoutMs"] = 60_000,
			});
			Assert.That(opened.IsError, Is.False, JsonSerializer.Serialize(opened));
			using var openJson = JsonDocument.Parse(JsonSerializer.Serialize(opened.StructuredContent));
			contextId = openJson.RootElement.GetProperty("contextId").GetString();
			Assert.That(contextId, Does.StartWith("ctx_"));

			var observed = await mcp.CallRawAsync("deepflow_observe", new Dictionary<string, object?>
			{
				["contextId"] = contextId,
				["includeElements"] = true,
				["refresh"] = true,
			});
			Assert.That(observed.IsError, Is.False);
			Assert.That(observed.Content.OfType<TextContentBlock>(), Is.Not.Empty);
			using var observeJson = JsonDocument.Parse(JsonSerializer.Serialize(observed.StructuredContent));
			var observedElements = observeJson.RootElement.GetProperty("elements");
			Assert.That(observedElements.GetArrayLength(), Is.GreaterThan(0));
			var observedHandles = observedElements.EnumerateArray().Select(static element => element.GetProperty("handle").GetString()).ToArray();
			Assert.That(observedHandles, Has.All.Not.Null.And.Not.Empty);
			Assert.That(observedHandles.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(observedHandles.Length));
			var snapshotUri = observed.Content.OfType<ResourceLinkBlock>().Single().Uri;
			Assert.That(snapshotUri, Does.Contain($"contexts/{contextId}/snapshots/"));
			Assert.That((await mcp.ReadResourceAsync(snapshotUri)).Contents, Is.Not.Empty);

			var found = await mcp.CallRawAsync("deepflow_find", new Dictionary<string, object?>
			{
				["contextId"] = contextId,
				["target"] = new Dictionary<string, object?> { ["kind"] = "semantic", ["automationId"] = "HelloWorldButton" },
			});
			Assert.That(found.IsError, Is.False);
			using var findJson = JsonDocument.Parse(JsonSerializer.Serialize(found.StructuredContent));
			var handle = findJson.RootElement.GetProperty("matches")[0].GetProperty("handle").GetString();

			var acted = await mcp.CallRawAsync("deepflow_act", new Dictionary<string, object?>
			{
				["contextId"] = contextId,
				["target"] = new Dictionary<string, object?> { ["kind"] = "handle", ["handle"] = handle },
				["action"] = new Dictionary<string, object?> { ["kind"] = "click" },
				["observe"] = "delta",
			});
			Assert.That(acted.IsError, Is.False);
			using var actJson = JsonDocument.Parse(JsonSerializer.Serialize(acted.StructuredContent));
			Assert.That(actJson.RootElement.GetProperty("delta").GetProperty("hasChanges").ValueKind, Is.EqualTo(JsonValueKind.True).Or.EqualTo(JsonValueKind.False));
			Assert.That(actJson.RootElement.GetProperty("elements").ValueKind, Is.EqualTo(JsonValueKind.Array));

			var captured = await mcp.CallRawAsync("deepflow_capture", new Dictionary<string, object?> { ["contextId"] = contextId });
			Assert.That(captured.IsError, Is.False);
			Assert.That(captured.Content.OfType<ImageContentBlock>().Single().MimeType, Is.EqualTo("image/png"));
			Assert.That(captured.Content.OfType<ResourceLinkBlock>(), Is.Not.Empty);
		}
		finally
		{
			if (contextId is not null)
				_ = await mcp.CallRawAsync("deepflow_close_context", new Dictionary<string, object?> { ["contextId"] = contextId });
		}
	}

	[Test]
	public async Task McpLaunchWorkflowDrivesHelloWorldThroughMcpTools()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync("--allow-launch", "--allow-actions", "--timeout-ms", "60000", "--attach-timeout-ms", "60000", "--cache-ttl-ms", "60000");

		var launch = await mcp.CallOkAsync("deepflow_launch_target", new Dictionary<string, object?>
		{
			["fileName"] = McpEndToEndHarness.ResolveHelloWorldExecutablePath(),
			["attachTimeoutMs"] = 60_000,
			["terminateOnDetach"] = true,
		});
		AssertAttached(launch.Target, launchedByServer: true);
		var processId = launch.Target.GetPropertyIgnoreCase("processId").GetInt32();

		try
		{
			await mcp.CallOkAsync("deepflow_ping_target");

			var tree = await mcp.CallOkAsync("deepflow_get_visual_tree", new Dictionary<string, object?>
			{
				["properties"] = DefaultProperties,
				["limit"] = 500,
				["refresh"] = true,
				["outputFormat"] = "json",
			});
			Assert.That(TreeContainsAutomationId(tree.Data, "HelloWorldWindow"), Is.True);
			Assert.That(TreeContainsAutomationId(tree.Data, "HelloWorldButton"), Is.True);

			var window = FindNodeByAutomationId(tree.Data, "HelloWorldWindow");
			var screenshot = await mcp.CallOkAsync("deepflow_capture_screenshot", new Dictionary<string, object?>
			{
				["targetId"] = window.GetPropertyIgnoreCase("targetId").GetString(),
				["includeBase64"] = true,
			});
			var screenshotData = screenshot.Data.GetPropertyIgnoreCase("screenshot");
			Assert.That(screenshotData.GetPropertyIgnoreCase("byteCount").GetInt32(), Is.GreaterThan(0));
			Assert.That(screenshotData.GetPropertyIgnoreCase("bytesBase64").GetString(), Is.Not.Null.And.Not.Empty);

			var button = await mcp.FindOneByAutomationIdAsync("HelloWorldButton", DefaultProperties, refresh: false);
			var textBox = FindNodeByAutomationId(tree.Data, "TextBox1");
			var expander = FindNodeByAutomationId(tree.Data, "ExpanderControl");
			var toggle = FindNodeByAutomationId(tree.Data, "TogglePopupButton");
			Assert.That(button.GetPropertyIgnoreCase("typeName").GetString(), Is.EqualTo("Button"));

			var bindingFailures = await mcp.CallOkAsync("deepflow_get_binding_failures", new Dictionary<string, object?>
			{
				["maxCount"] = 25,
			});
			Assert.That(bindingFailures.Data.ValueKind, Is.EqualTo(JsonValueKind.Object));

			await AssertVisualTreeStreamProducesFrame(mcp);

			await mcp.CallOkAsync("deepflow_click_element", new Dictionary<string, object?>
			{
				["targetId"] = toggle.GetPropertyIgnoreCase("targetId").GetString(),
			});
			await mcp.CallOkAsync("deepflow_wait_for_element", new Dictionary<string, object?>
			{
				["automationId"] = "TogglePopupButton",
				["property"] = "IsChecked=True",
				["properties"] = DefaultProperties,
				["timeoutMs"] = 30_000,
				["intervalMs"] = 250,
			});

			await mcp.CallOkAsync("deepflow_focus_element", new Dictionary<string, object?>
			{
				["targetId"] = textBox.GetPropertyIgnoreCase("targetId").GetString(),
				["after"] = "none",
			});
			await mcp.CallOkAsync("deepflow_wait_for_element", new Dictionary<string, object?>
			{
				["automationId"] = "TextBox1",
				["property"] = "IsKeyboardFocused=True",
				["properties"] = DefaultProperties,
				["timeoutMs"] = 30_000,
				["intervalMs"] = 250,
			});

			var typedText = $"mcp-type-{Guid.NewGuid():N}";
			await mcp.CallOkAsync("deepflow_type_text", new Dictionary<string, object?>
			{
				["targetId"] = textBox.GetPropertyIgnoreCase("targetId").GetString(),
				["text"] = typedText,
				["clearFirst"] = true,
				["after"] = "none",
			});

			var setText = $"mcp-set-{Guid.NewGuid():N}";
			await mcp.CallOkAsync("deepflow_set_property", new Dictionary<string, object?>
			{
				["targetId"] = textBox.GetPropertyIgnoreCase("targetId").GetString(),
				["propertyName"] = "Text",
				["value"] = setText,
				["after"] = "none",
			});

			var replacementText = $"mcp-key-{Guid.NewGuid():N}";
			await mcp.CallOkAsync("deepflow_press_keys", new Dictionary<string, object?>
			{
				["targetId"] = textBox.GetPropertyIgnoreCase("targetId").GetString(),
				["keys"] = "Control+A",
				["delayMs"] = 1,
				["after"] = "none",
			});
			await mcp.CallOkAsync("deepflow_type_text", new Dictionary<string, object?>
			{
				["targetId"] = textBox.GetPropertyIgnoreCase("targetId").GetString(),
				["text"] = replacementText,
				["after"] = "none",
			});

			await mcp.CallOkAsync("deepflow_invoke_operation", new Dictionary<string, object?>
			{
				["targetId"] = expander.GetPropertyIgnoreCase("targetId").GetString(),
				["operation"] = "Expand",
				["after"] = "none",
			});
			await mcp.CallOkAsync("deepflow_wait_for_element", new Dictionary<string, object?>
			{
				["automationId"] = "ExpanderControl",
				["property"] = "IsExpanded=True",
				["properties"] = DefaultProperties,
				["timeoutMs"] = 30_000,
				["intervalMs"] = 250,
			});

			await mcp.CallOkAsync("deepflow_raise_event", new Dictionary<string, object?>
			{
				["targetId"] = button.GetPropertyIgnoreCase("targetId").GetString(),
				["eventName"] = "Click",
			});
		}
		finally
		{
			await mcp.CallOkAsync("deepflow_detach_target");
			DesktopHarnessProcess.WaitForProcessExit(processId, TimeSpan.FromSeconds(10));
		}
	}

	[Test]
	public async Task McpCanAttachToAlreadyRunningHelloWorldThroughMcpTools()
	{
		using var harness = DesktopHarnessProcess.Start(McpEndToEndHarness.ResolveHelloWorldExecutablePath());
		await using var mcp = await McpEndToEndHarness.StartAsync("--allow-actions", "--timeout-ms", "60000", "--attach-timeout-ms", "60000", "--cache-ttl-ms", "60000");

		var attach = await mcp.CallOkAsync("deepflow_attach_target", new Dictionary<string, object?>
		{
			["pid"] = harness.Process.Id,
			["timeoutMs"] = 60_000,
		});
		AssertAttached(attach.Target, launchedByServer: false);

		await mcp.CallOkAsync("deepflow_ping_target");
		await mcp.CallOkAsync("deepflow_get_visual_tree", new Dictionary<string, object?>
		{
			["properties"] = DefaultProperties,
			["limit"] = 500,
			["refresh"] = true,
			["outputFormat"] = "json",
		});
		var button = await mcp.FindOneByAutomationIdAsync("HelloWorldButton", DefaultProperties, refresh: false);
		Assert.That(button.GetPropertyIgnoreCase("typeName").GetString(), Is.EqualTo("Button"));

		await mcp.CallOkAsync("deepflow_detach_target");
		harness.Process.Refresh();
		Assert.That(harness.Process.HasExited, Is.False, "Detaching from an externally started harness must not terminate it.");
	}

	[Test]
	public async Task McpAttachedReadAndStreamToolsCanRunRepeatedly()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync("--allow-launch", "--timeout-ms", "60000", "--attach-timeout-ms", "60000", "--cache-ttl-ms", "60000");

		var launch = await mcp.CallOkAsync("deepflow_launch_target", new Dictionary<string, object?>
		{
			["fileName"] = McpEndToEndHarness.ResolveHelloWorldExecutablePath(),
			["attachTimeoutMs"] = 60_000,
			["terminateOnDetach"] = true,
		});
		AssertAttached(launch.Target, launchedByServer: true);
		var processId = launch.Target.GetPropertyIgnoreCase("processId").GetInt32();

		try
		{
			var tree = await mcp.CallOkAsync("deepflow_get_visual_tree", new Dictionary<string, object?>
			{
				["properties"] = DefaultProperties,
				["limit"] = 500,
				["refresh"] = true,
				["outputFormat"] = "json",
			});
			var button = FindNodeByAutomationId(tree.Data, "HelloWorldButton");
			var targetId = button.GetPropertyIgnoreCase("targetId").GetString();
			Assert.That(targetId, Is.Not.Null.And.Not.Empty);

			for (var i = 0; i < 5; i++)
			{
				await mcp.CallOkAsync("deepflow_target_status");
				await mcp.CallOkAsync("deepflow_ping_target");
				await mcp.CallOkAsync("deepflow_suggest_selectors", new Dictionary<string, object?>
				{
					["targetId"] = targetId,
					["refresh"] = i % 2 == 0,
				});
				await AssertVisualTreeStreamProducesFrame(mcp);
			}
		}
		finally
		{
			await mcp.CallOkAsync("deepflow_detach_target");
			DesktopHarnessProcess.WaitForProcessExit(processId, TimeSpan.FromSeconds(10));
		}
	}

	private static async Task AssertVisualTreeStreamProducesFrame(McpEndToEndHarness mcp)
	{
		string? streamId = null;
		try
		{
			var start = await mcp.CallOkAsync("deepflow_start_stream", new Dictionary<string, object?>
			{
				["kind"] = "visual-tree",
				["intervalMs"] = 250,
				["properties"] = DefaultProperties,
			});
			streamId = start.Data.GetPropertyIgnoreCase("streamId").GetString();
			Assert.That(streamId, Is.Not.Null.And.Not.Empty);

			var frame = await mcp.WaitForStreamFrameAsync(streamId!);
			Assert.That(frame.GetPropertyIgnoreCase("streamKind").GetString(), Is.EqualTo("visual-tree"));
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(streamId))
			{
				await mcp.CallOkAsync("deepflow_stop_stream", new Dictionary<string, object?>
				{
					["streamId"] = streamId,
				});
			}
		}
	}

	private static void AssertAttached(JsonElement target, bool launchedByServer)
	{
		Assert.That(target.GetPropertyIgnoreCase("attached").GetBoolean(), Is.True);
		Assert.That(target.GetPropertyIgnoreCase("isAlive").GetBoolean(), Is.True);
		Assert.That(target.GetPropertyIgnoreCase("launchedByServer").GetBoolean(), Is.EqualTo(launchedByServer));
		Assert.That(target.GetPropertyIgnoreCase("processId").GetInt32(), Is.GreaterThan(0));
	}

	private static bool TreeContainsAutomationId(JsonElement tree, string automationId)
	{
		return TryFindNodeByAutomationId(tree, automationId, out _);
	}

	private static JsonElement FindNodeByAutomationId(JsonElement tree, string automationId)
	{
		if (TryFindNodeByAutomationId(tree, automationId, out var node))
			return node;

		throw new AssertionException($"Expected visual tree to contain AutomationId '{automationId}'.");
	}

	private static bool TryFindNodeByAutomationId(JsonElement tree, string automationId, out JsonElement match)
	{
		foreach (var node in tree.GetPropertyIgnoreCase("nodes").EnumerateArray())
		{
			if (!node.TryGetPropertyIgnoreCase("properties", out var properties))
				continue;

			if (properties.TryGetPropertyIgnoreCase("AutomationProperties.AutomationId", out var value)
				&& string.Equals(value.GetString(), automationId, StringComparison.Ordinal))
			{
				match = node;
				return true;
			}
		}

		match = default;
		return false;
	}
}
