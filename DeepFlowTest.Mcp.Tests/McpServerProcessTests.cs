namespace DeepFlowTest.Mcp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DeepFlowTest.Mcp.Resources;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

[TestFixture]
public sealed class McpServerProcessTests
{
	[Test]
	public async Task ServerStartsAsDesktopHttpAppAndWritesEndpointFile()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync();
		await mcp.PingAsync();

		using var document = JsonDocument.Parse(File.ReadAllText(mcp.EndpointFilePath));
		var root = document.RootElement;
		Assert.That(root.GetProperty("streamableHttpUrl").GetString(), Is.EqualTo(mcp.Endpoint.ToString().TrimEnd('/')));
		Assert.That(root.GetProperty("legacySseUrl").ValueKind, Is.EqualTo(JsonValueKind.Null));
		Assert.That(root.GetProperty("processId").GetInt32(), Is.EqualTo(mcp.ServerProcessId));
		Assert.That(root.GetProperty("startedAtUtc").GetDateTimeOffset(), Is.LessThanOrEqualTo(DateTimeOffset.UtcNow));
	}

	[Test]
	public async Task ServerInitializesThroughMcpClient()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync();

		await mcp.PingAsync();

		var tools = await mcp.ListToolsAsync();
		Assert.That(tools.Select(static tool => tool.Name), Does.Contain("deepflow_target_status"));
		Assert.That(tools.Select(static tool => tool.Name), Does.Contain("deepflow_configure_diagnostics"));
		AssertTargetToolSchemasAreClientFriendly(tools);

		var status = await mcp.CallAsync("deepflow_target_status");
		Assert.That(status.Success, Is.True);
		await AssertTargetToolsCanBeCalled(mcp);

		var prompts = await mcp.ListPromptsAsync();
		Assert.That(prompts.Select(static prompt => prompt.Name), Does.Contain("inspect_ui"));

		var promptResult = await mcp.GetPromptAsync("inspect_ui");
		Assert.That(promptResult.Messages, Is.Not.Empty);

		var resources = await mcp.ListResourcesAsync();
		Assert.That(resources.Select(static resource => resource.Uri?.ToString()), Does.Contain(DeepFlowResourceNames.TargetStatus));
		Assert.That(resources.Select(static resource => resource.Uri?.ToString()), Does.Contain(DeepFlowResourceNames.RecentActivity));

		var resourceResult = await mcp.ReadResourceAsync(DeepFlowResourceNames.TargetStatus);
		Assert.That(resourceResult.Contents, Is.Not.Empty);
	}

	[Test]
	public async Task AgentProfileExposesOnlyStructuredAgentTools()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync("--tool-profile", "agent");

		var tools = await mcp.ListToolsAsync();
		var expected = new[]
		{
			"deepflow_open_context",
			"deepflow_observe",
			"deepflow_find",
			"deepflow_act",
			"deepflow_wait",
			"deepflow_capture",
			"deepflow_diagnose",
			"deepflow_close_context",
		};

		Assert.That(tools.Select(static tool => tool.Name), Is.EquivalentTo(expected));
		foreach (var tool in tools)
		{
			Assert.That(tool.ProtocolTool.OutputSchema, Is.Not.Null, $"{tool.Name} should advertise an output schema.");
			Assert.That(tool.Description, Is.Not.Null.And.Not.Empty);
		}

		var observe = tools.Single(tool => tool.Name == "deepflow_observe");
		var schema = observe.JsonSchema.ToString();
		Assert.That(schema, Does.Contain("contextId"));
		Assert.That(schema, Does.Contain("properties"));
		Assert.That(schema, Does.Contain("Maximum number of nodes"));
		Assert.That(observe.ProtocolTool.OutputSchema!.ToString(), Does.Contain("elements"));

		var openSchema = tools.Single(tool => tool.Name == "deepflow_open_context").JsonSchema.ToString();
		Assert.That(openSchema, Does.Contain("anyOf"));
		Assert.That(openSchema, Does.Contain("attach"));
		Assert.That(openSchema, Does.Contain("launch"));

		var actSchema = tools.Single(tool => tool.Name == "deepflow_act").JsonSchema.ToString();
		Assert.That(actSchema, Does.Contain("anyOf"));
		Assert.That(actSchema, Does.Contain("click"));
		Assert.That(actSchema, Does.Contain("drag"));
		Assert.That(actSchema, Does.Contain("semantic"));
		Assert.That(actSchema, Does.Contain("handle"));
		Assert.That(actSchema, Does.Contain("Ambiguous selectors fail by default"));
		Assert.That(actSchema, Does.Not.Contain("textValue"), "Computed helpers must not leak into the agent input schema.");
		Assert.That(tools.Single(tool => tool.Name == "deepflow_act").ProtocolTool.OutputSchema!.ToString(), Does.Contain("delta"));

		var resources = await mcp.ListResourcesAsync();
		Assert.That(resources.Select(static resource => resource.Uri?.ToString()), Does.Not.Contain(DeepFlowResourceNames.LatestVisualTree));
		Assert.That(resources.Select(static resource => resource.Uri?.ToString()), Does.Not.Contain(DeepFlowResourceNames.LatestScreenshot));
		var templates = await mcp.ListResourceTemplatesAsync();
		Assert.That(templates.Select(static template => template.UriTemplate), Is.EquivalentTo(new[]
		{
			"deepflow://contexts/{contextId}/snapshots/{artifactId}",
			"deepflow://contexts/{contextId}/screenshots/{artifactId}",
			"deepflow://contexts/{contextId}/diagnostics/{diagnosticKind}/{artifactId}",
		}));
	}

	[Test]
	public async Task AgentToolExecutionFailuresUseMcpIsError()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync("--tool-profile", "agent");

		var result = await mcp.CallRawAsync("deepflow_observe", new Dictionary<string, object?>
		{
			["contextId"] = "ctx_missing",
		});

		Assert.That(result.IsError, Is.True);
		Assert.That(result.StructuredContent, Is.Not.Null);
		Assert.That(result.Content.OfType<TextContentBlock>().Single().Text, Does.Contain("stale_context"));
		using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.StructuredContent));
		Assert.That(json.RootElement.GetProperty("recovery").GetProperty("kind").GetString(), Is.EqualTo("open_context"));
	}

	[Test]
	public async Task RecentActivityResourceCapturesHttpToolCalls()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync();

		await mcp.CallOkAsync("deepflow_list_processes", new Dictionary<string, object?>
		{
			["candidatesOnly"] = false,
		});
		var resourceResult = await mcp.ReadResourceAsync(DeepFlowResourceNames.RecentActivity);
		var text = ReadResourceText(resourceResult);

		Assert.That(text, Does.Contain("tool.start"));
		Assert.That(text, Does.Contain("tool.success"));
		Assert.That(text, Does.Contain("ListProcesses"));
		Assert.That(text, Does.Contain("parameters"));
		Assert.That(text, Does.Contain("candidatesOnly"));
		Assert.That(text, Does.Contain("false"));
		Assert.That(text, Does.Contain("result"));
		Assert.That(text, Does.Contain("processes"));
	}

	[Test]
	public async Task SimpleHttpRequestsCanRunRepeatedlyWithoutMcpLevelErrors()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync();

		for (var i = 0; i < 10; i++)
		{
			await mcp.PingAsync();
			var status = await mcp.CallAsync("deepflow_target_status");
			Assert.That(status.Success, Is.True, $"target_status failed on iteration {i}: {status.ErrorSummary}");

			var resources = await mcp.ListResourcesAsync();
			Assert.That(resources.Select(static resource => resource.Uri?.ToString()), Does.Contain(DeepFlowResourceNames.RecentActivity));

			var activity = await mcp.ReadResourceAsync(DeepFlowResourceNames.RecentActivity);
			Assert.That(ReadResourceText(activity), Does.Contain("TargetStatus"));
		}
	}

	[Test]
	public async Task HttpServerRejectsNonLoopbackHostHeader()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync();
		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, mcp.Endpoint);
		request.Headers.Host = "example.com";

		using var response = await client.SendAsync(request);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
	}

	[Test]
	public async Task HttpServerRejectsDisallowedBrowserOrigin()
	{
		await using var mcp = await McpEndToEndHarness.StartAsync();
		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, mcp.Endpoint);
		request.Headers.Add("Origin", "https://example.com");

		using var response = await client.SendAsync(request);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
		Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Origin is not allowed"));
	}

	private static void AssertTargetToolSchemasAreClientFriendly(IList<McpClientTool> tools)
	{
		var targetToolNames = new[]
		{
			"deepflow_list_processes",
			"deepflow_attach_target",
			"deepflow_launch_target",
			"deepflow_detach_target",
			"deepflow_target_status",
			"deepflow_ping_target",
		};

		foreach (var toolName in targetToolNames)
		{
			var tool = tools.SingleOrDefault(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));
			Assert.That(tool, Is.Not.Null, $"Expected MCP tool '{toolName}' to be registered.");
			Assert.That(tool!.Description, Is.Not.Null.And.Not.Empty, $"Expected MCP tool '{toolName}' to include a description.");

			var schema = tool.JsonSchema.ToString();
			Assert.That(schema, Is.Not.Empty, $"Expected MCP tool '{toolName}' to expose an input schema.");
			Assert.That(schema, Does.Not.Contain("McpTargetSelector"), $"Tool '{toolName}' should expose simple arguments, not internal selector DTOs.");
			Assert.That(schema, Does.Not.Contain("McpLaunchOptions"), $"Tool '{toolName}' should expose simple arguments, not internal launch DTOs.");
			Assert.That(schema, Does.Not.Contain("McpToolResponse"), $"Tool '{toolName}' should expose protocol-friendly schemas, not internal response DTOs.");
		}
	}

	private static async Task AssertTargetToolsCanBeCalled(McpEndToEndHarness mcp)
	{
		var calls = new (string Name, IReadOnlyDictionary<string, object?> Arguments)[]
		{
			("deepflow_list_processes", new Dictionary<string, object?>()),
			("deepflow_attach_target", new Dictionary<string, object?>()),
			("deepflow_launch_target", new Dictionary<string, object?> { ["fileName"] = "missing.exe" }),
			("deepflow_detach_target", new Dictionary<string, object?>()),
			("deepflow_target_status", new Dictionary<string, object?>()),
			("deepflow_ping_target", new Dictionary<string, object?>()),
		};

		foreach (var call in calls)
		{
			var result = await mcp.CallAsync(call.Name, call.Arguments);
			Assert.That(result.Payload.ValueKind, Is.Not.EqualTo(System.Text.Json.JsonValueKind.Undefined), $"Tool '{call.Name}' should return a structured DeepFlowTest result instead of an MCP-level error.");
		}
	}

	private static string ReadResourceText(ReadResourceResult result)
	{
		foreach (var content in result.Contents)
		{
			if (content is TextResourceContents textContent)
				return textContent.Text;
		}

		Assert.Fail("Expected a text resource result.");
		return string.Empty;
	}
}
