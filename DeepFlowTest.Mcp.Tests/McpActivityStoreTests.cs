namespace DeepFlowTest.Mcp.Tests;

using System;
using System.IO;
using System.Text.Json;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using Microsoft.Extensions.Options;
using NUnit.Framework;

[TestFixture]
public sealed class McpActivityStoreTests
{
	[Test]
	public void OptionalActivityLogWritesJsonLinesForPostRunAnalysis()
	{
		var directory = Path.Combine(Path.GetTempPath(), "DeepFlowTest.Mcp.Tests", Guid.NewGuid().ToString("N"));
		var path = Path.Combine(directory, "activity.jsonl");
		try
		{
			var store = new McpActivityStore(Options.Create(new McpServerOptions { ActivityLogFile = path }));
			store.Publish(new McpActivityEvent
			{
				Source = "client",
				Kind = "tool.success",
				Name = "Observe",
				Status = "success",
				Details = new { contextId = "ctx_test" },
			});

			var lines = File.ReadAllLines(path);
			Assert.That(lines, Has.Exactly(1).Items);
			using var json = JsonDocument.Parse(lines[0]);
			Assert.That(json.RootElement.GetProperty("sequence").GetInt64(), Is.EqualTo(1));
			Assert.That(json.RootElement.GetProperty("kind").GetString(), Is.EqualTo("tool.success"));
			Assert.That(json.RootElement.GetProperty("details").GetProperty("contextId").GetString(), Is.EqualTo("ctx_test"));
		}
		finally
		{
			if (Directory.Exists(directory))
				Directory.Delete(directory, recursive: true);
		}
	}
}
