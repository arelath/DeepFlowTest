namespace DeepFlowTest.Mcp.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.Mcp.Resources;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using NUnit.Framework;

[TestFixture]
public sealed class McpServerProcessTests
{
	[Test]
	public void ServerStartupDoesNotWriteDiagnosticsToStdout()
	{
		var executablePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "DeepFlowTest.Mcp.exe");
		Assert.That(File.Exists(executablePath), Is.True, "The MCP apphost must be present in the test output directory.");

		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = executablePath,
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		});
		Assert.That(process, Is.Not.Null);

		process!.StandardInput.Close();
		if (!process.WaitForExit(3_000))
		{
			process.Kill(entireProcessTree: true);
			Assert.Fail("MCP server did not exit after stdin was closed.");
		}

		var stdout = process.StandardOutput.ReadToEnd();
		Assert.That(stdout, Is.Empty);
	}

	[Test]
	public async Task ServerInitializesThroughMcpClient()
	{
		var executablePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "DeepFlowTest.Mcp.exe");
		Assert.That(File.Exists(executablePath), Is.True, "The MCP apphost must be present in the test output directory.");

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		await using var client = await McpClient.CreateAsync(
			new StdioClientTransport(
				new StdioClientTransportOptions
				{
					Command = executablePath,
					WorkingDirectory = TestContext.CurrentContext.TestDirectory,
					ShutdownTimeout = TimeSpan.FromSeconds(2),
				},
				NullLoggerFactory.Instance),
			clientOptions: null,
			loggerFactory: NullLoggerFactory.Instance,
			cancellationToken: cts.Token);

		await client.PingAsync(cancellationToken: cts.Token);

		var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
		Assert.That(tools.Select(static tool => tool.Name), Does.Contain("deepflow_target_status"));
		AssertTargetToolSchemasAreClientFriendly(tools);

		var status = await client.CallToolAsync("deepflow_target_status", cancellationToken: cts.Token);
		Assert.That(status.IsError, Is.Not.True);
		await AssertTargetToolsCanBeCalled(client, cts.Token);

		var prompts = await client.ListPromptsAsync(cancellationToken: cts.Token);
		Assert.That(prompts.Select(static prompt => prompt.Name), Does.Contain("inspect_ui"));

		var promptResult = await client.GetPromptAsync("inspect_ui", cancellationToken: cts.Token);
		Assert.That(promptResult.Messages, Is.Not.Empty);

		var resources = await client.ListResourcesAsync(cancellationToken: cts.Token);
		Assert.That(resources.Select(static resource => resource.Uri?.ToString()), Does.Contain(DeepFlowResourceNames.TargetStatus));

		var resourceResult = await client.ReadResourceAsync(DeepFlowResourceNames.TargetStatus, cancellationToken: cts.Token);
		Assert.That(resourceResult.Contents, Is.Not.Empty);
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

	private static async Task AssertTargetToolsCanBeCalled(McpClient client, CancellationToken cancellationToken)
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
			var result = await client.CallToolAsync(call.Name, call.Arguments, cancellationToken: cancellationToken);
			Assert.That(result.IsError, Is.Not.True, $"Tool '{call.Name}' should return a structured DeepFlowTest result instead of an MCP-level error.");
		}
	}
}
