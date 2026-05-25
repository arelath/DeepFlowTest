namespace DeepFlowTest.Mcp.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

internal sealed class McpEndToEndHarness : IAsyncDisposable
{
	private static readonly IReadOnlyDictionary<string, object?> EmptyArguments = new Dictionary<string, object?>();

	private readonly CancellationTokenSource timeout;
	private readonly List<string> stderr = [];
	private readonly object stderrGate = new();
	private readonly McpClient client;
	private readonly Process process;
	private readonly string endpointFile;

	private McpEndToEndHarness(McpClient client, Uri endpoint, Process process, string endpointFile, CancellationTokenSource timeout)
	{
		this.client = client;
		Endpoint = endpoint;
		this.process = process;
		this.endpointFile = endpointFile;
		this.timeout = timeout;
	}

	public static async Task<McpEndToEndHarness> StartAsync(params string[] serverArguments)
	{
		var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
		var stderr = new List<string>();
		var stderrGate = new object();
		var endpointFile = Path.Combine(Path.GetTempPath(), "DeepFlowTest.Mcp.Tests", Guid.NewGuid().ToString("N"), "endpoint.json");
		Process? process = null;
		try
		{
			var arguments = new List<string>
			{
				"--http-port",
				"0",
				"--endpoint-file",
				endpointFile,
				"--start-minimized",
			};
			arguments.AddRange(serverArguments);
			process = StartServerProcess(arguments, stderr, stderrGate);
			var endpoint = await WaitForEndpointAsync(endpointFile, process, timeout.Token);

			var client = await McpClient.CreateAsync(
				new HttpClientTransport(
					new HttpClientTransportOptions
					{
						Endpoint = endpoint,
						TransportMode = HttpTransportMode.StreamableHttp,
					},
					NullLoggerFactory.Instance),
				clientOptions: null,
				loggerFactory: NullLoggerFactory.Instance,
				cancellationToken: timeout.Token);

			var harness = new McpEndToEndHarness(client, endpoint, process, endpointFile, timeout);
			lock (stderrGate)
			{
				harness.stderr.AddRange(stderr);
			}

			return harness;
		}
		catch
		{
			if (process is not null)
				StopServerProcess(process);

			timeout.Dispose();
			throw;
		}
	}

	public CancellationToken CancellationToken => timeout.Token;

	public Uri Endpoint { get; }

	public string EndpointFilePath => endpointFile;

	public int ServerProcessId => process.Id;

	public async Task<McpToolJsonResult> CallOkAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null)
	{
		var result = await CallAsync(toolName, arguments);
		Assert.That(result.Success, Is.True, $"{toolName} failed: {result.ErrorSummary}");
		return result;
	}

	public async Task<McpToolJsonResult> CallAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null)
	{
		var result = await client.CallToolAsync(toolName, arguments ?? EmptyArguments, cancellationToken: timeout.Token);
		return McpToolJsonResult.From(toolName, result, ReadStderrTail());
	}

	public async Task<IList<McpClientTool>> ListToolsAsync() =>
		await client.ListToolsAsync(cancellationToken: timeout.Token);

	public async Task<IList<McpClientPrompt>> ListPromptsAsync() =>
		await client.ListPromptsAsync(cancellationToken: timeout.Token);

	public async Task<GetPromptResult> GetPromptAsync(string name) =>
		await client.GetPromptAsync(name, cancellationToken: timeout.Token);

	public async Task<IList<McpClientResource>> ListResourcesAsync() =>
		await client.ListResourcesAsync(cancellationToken: timeout.Token);

	public async Task<ReadResourceResult> ReadResourceAsync(string uri) =>
		await client.ReadResourceAsync(uri, cancellationToken: timeout.Token);

	public async Task PingAsync() =>
		await client.PingAsync(cancellationToken: timeout.Token);

	public async Task<McpToolJsonResult> WaitForElementTextAsync(string automationId, string expectedText, int timeoutMs = 30_000) =>
		await CallOkAsync("deepflow_wait_for_element", new Dictionary<string, object?>
		{
			["automationId"] = automationId,
			["text"] = expectedText,
			["timeoutMs"] = timeoutMs,
			["intervalMs"] = 250,
		});

	public async Task<JsonElement> FindOneByAutomationIdAsync(string automationId, string? properties = null, bool refresh = true)
	{
		var result = await CallOkAsync("deepflow_find_elements", new Dictionary<string, object?>
		{
			["automationId"] = automationId,
			["limit"] = 1,
			["includeProperties"] = true,
			["properties"] = properties,
			["refresh"] = refresh,
		});

		var matches = result.Data.GetPropertyIgnoreCase("matches");
		Assert.That(matches.GetArrayLength(), Is.EqualTo(1), $"Expected exactly one element with AutomationId '{automationId}'.");
		return matches[0].GetPropertyIgnoreCase("node");
	}

	public async Task<JsonElement> WaitForStreamFrameAsync(string streamId, int timeoutMs = 10_000)
	{
		var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
		while (DateTimeOffset.UtcNow <= deadline)
		{
			var read = await CallOkAsync("deepflow_read_stream", new Dictionary<string, object?>
			{
				["streamId"] = streamId,
				["maxFrames"] = 5,
			});

			var frames = read.Data.GetPropertyIgnoreCase("frames");
			if (frames.GetArrayLength() > 0)
				return frames[0];

			await Task.Delay(150, timeout.Token);
		}

		Assert.Fail($"Stream '{streamId}' did not produce a frame within {timeoutMs} ms.");
		return default;
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			await CallAsync("deepflow_detach_target");
		}
		catch
		{
		}

		await client.DisposeAsync();
		StopServerProcess(process);
		TryDeleteEndpointFile(endpointFile);
		timeout.Dispose();
	}

	private IReadOnlyList<string> ReadStderrTail()
	{
		lock (stderrGate)
			return stderr.TakeLast(20).ToArray();
	}

	public static string ResolveHelloWorldExecutablePath()
	{
		var path = Path.Combine(
			FindRepositoryRoot(),
			"TestHarnesses",
			"bin",
			"HelloWorld",
			"Debug",
			"net8.0-windows",
			"HelloWorld.exe");

		Assert.That(File.Exists(path), Is.True, $"HelloWorld harness was not found at '{path}'. Build the test project first.");
		return path;
	}

	private static string ResolveMcpExecutablePath()
	{
		var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "DeepFlowTest.Mcp.exe");
		Assert.That(File.Exists(path), Is.True, "The MCP apphost must be present in the test output directory.");
		return path;
	}

	private static Process StartServerProcess(
		IReadOnlyList<string> arguments,
		List<string> stderr,
		object stderrGate)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = ResolveMcpExecutablePath(),
			WorkingDirectory = TestContext.CurrentContext.TestDirectory,
			UseShellExecute = false,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		var process = new Process
		{
			StartInfo = startInfo,
			EnableRaisingEvents = true,
		};
		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data is null)
				return;

			lock (stderrGate)
				stderr.Add(e.Data);
		};
		Assert.That(process.Start(), Is.True, "Failed to start MCP server process.");
		process.BeginErrorReadLine();
		return process;
	}

	private static async Task<Uri> WaitForEndpointAsync(string endpointFile, Process process, CancellationToken cancellationToken)
	{
		var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
		while (DateTimeOffset.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (process.HasExited)
				Assert.Fail($"MCP server exited before writing endpoint file. Exit code: {process.ExitCode}");

			if (File.Exists(endpointFile))
			{
				using var stream = File.OpenRead(endpointFile);
				var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
				var endpoint = json.RootElement.GetProperty("streamableHttpUrl").GetString();
				Assert.That(endpoint, Is.Not.Null.And.Not.Empty);
				return new Uri(endpoint!);
			}

			await Task.Delay(100, cancellationToken);
		}

		Assert.Fail($"MCP server did not write endpoint file '{endpointFile}' within 30 seconds.");
		return new Uri("http://127.0.0.1/");
	}

	private static void TryDeleteEndpointFile(string endpointFile)
	{
		try
		{
			File.Delete(endpointFile);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private static void StopServerProcess(Process process)
	{
		if (process.HasExited)
			return;

		try
		{
			process.CloseMainWindow();
			if (process.WaitForExit(5_000))
				return;
		}
		catch (InvalidOperationException)
		{
			return;
		}

		if (!process.HasExited)
			process.Kill(entireProcessTree: true);
	}

	private static string FindRepositoryRoot()
	{
		var directory = TestContext.CurrentContext.TestDirectory;
		while (!string.IsNullOrWhiteSpace(directory))
		{
			if (File.Exists(Path.Combine(directory, "DeepFlowTest.sln")))
				return directory;

			directory = Directory.GetParent(directory)?.FullName;
		}

		throw new DirectoryNotFoundException("Could not find the repository root.");
	}
}

internal sealed class McpToolJsonResult
{
	private McpToolJsonResult(string toolName, JsonElement payload, IReadOnlyList<string> stderr)
	{
		ToolName = toolName;
		Payload = payload;
		Stderr = stderr;
	}

	public string ToolName { get; }

	public JsonElement Payload { get; }

	public IReadOnlyList<string> Stderr { get; }

	public bool Success => Payload.GetPropertyIgnoreCase("success").GetBoolean();

	public JsonElement Data => Payload.TryGetPropertyIgnoreCase("data", out var data) ? data : default;

	public JsonElement Target => Payload.TryGetPropertyIgnoreCase("target", out var target) ? target : default;

	public string ErrorSummary
	{
		get
		{
			if (Payload.TryGetPropertyIgnoreCase("error", out var error) && error.ValueKind == JsonValueKind.Object)
			{
				var code = error.TryGetPropertyIgnoreCase("code", out var codeElement) ? codeElement.GetString() : null;
				var message = error.TryGetPropertyIgnoreCase("message", out var messageElement) ? messageElement.GetString() : null;
				var stderr = Stderr.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, Stderr);
				return $"{code}: {message}{stderr}";
			}

			return string.Join(Environment.NewLine, Stderr);
		}
	}

	public static McpToolJsonResult From(string toolName, CallToolResult result, IReadOnlyList<string> stderr)
	{
		Assert.That(result.IsError, Is.Not.True, $"{toolName} returned an MCP-level error: {ReadContentText(result)}");
		var payload = ParsePayload(result);
		return new McpToolJsonResult(toolName, payload, stderr);
	}

	private static JsonElement ParsePayload(CallToolResult result)
	{
		if (result.StructuredContent is not null)
			return JsonDocument.Parse(JsonSerializer.Serialize(result.StructuredContent)).RootElement.Clone();

		var text = ReadContentText(result);
		Assert.That(text, Is.Not.Null.And.Not.Empty, "Expected the tool response to contain JSON text content.");
		return JsonDocument.Parse(text!).RootElement.Clone();
	}

	private static string? ReadContentText(CallToolResult result)
	{
		var text = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;
		if (!string.IsNullOrWhiteSpace(text))
			return text;

		return result.Content is null ? null : JsonSerializer.Serialize(result.Content);
	}
}

internal static class JsonElementExtensions
{
	public static JsonElement GetPropertyIgnoreCase(this JsonElement element, string propertyName)
	{
		if (element.TryGetPropertyIgnoreCase(propertyName, out var value))
			return value;

		throw new KeyNotFoundException($"JSON property '{propertyName}' was not found in {element}.");
	}

	public static bool TryGetPropertyIgnoreCase(this JsonElement element, string propertyName, out JsonElement value)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in element.EnumerateObject())
			{
				if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
				{
					value = property.Value;
					return true;
				}
			}
		}

		value = default;
		return false;
	}
}

internal sealed class DesktopHarnessProcess : IDisposable
{
	private DesktopHarnessProcess(Process process)
	{
		Process = process;
	}

	public Process Process { get; }

	public static DesktopHarnessProcess Start(string executablePath)
	{
		var process = Process.Start(new ProcessStartInfo(executablePath)
		{
			UseShellExecute = false,
			WorkingDirectory = Path.GetDirectoryName(executablePath)!,
		}) ?? throw new InvalidOperationException($"Failed to start '{executablePath}'.");

		try
		{
			WaitForMainWindow(process, TimeSpan.FromSeconds(20));
			return new DesktopHarnessProcess(process);
		}
		catch
		{
			Stop(process);
			process.Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		Stop(Process);
		Process.Dispose();
	}

	public static void WaitForProcessExit(int processId, TimeSpan timeout)
	{
		Process process;
		try
		{
			process = Process.GetProcessById(processId);
		}
		catch (ArgumentException)
		{
			return;
		}

		using (process)
		{
			if (!process.WaitForExit((int)timeout.TotalMilliseconds))
				Assert.Fail($"Process {processId} did not exit within {timeout.TotalSeconds:0} seconds.");
		}
	}

	private static void WaitForMainWindow(Process process, TimeSpan timeout)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			if (process.HasExited)
				throw new InvalidOperationException($"Harness process exited with code {process.ExitCode} before creating a main window.");

			process.Refresh();
			if (process.MainWindowHandle != IntPtr.Zero)
				return;

			Thread.Sleep(100);
		}

		throw new TimeoutException($"Harness process did not create a main window within {timeout.TotalSeconds:0} seconds.");
	}

	private static void Stop(Process process)
	{
		if (process.HasExited)
			return;

		try
		{
			process.CloseMainWindow();
			if (process.WaitForExit(5_000))
				return;
		}
		catch (InvalidOperationException)
		{
			return;
		}

		if (!process.HasExited)
			process.Kill(entireProcessTree: true);
	}
}
