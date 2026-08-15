namespace DeepFlowTest.Mcp.Tests;

using System;
using System.IO;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class McpOptionsTests
{
	[Test]
	public void DefaultsAreConservative()
	{
		var options = new McpServerOptions();

		Assert.That(options.Policy.AllowLaunch, Is.False);
		Assert.That(options.Policy.AllowActions, Is.False);
		Assert.That(options.Policy.AllowArbitraryInvoke, Is.False);
		Assert.That(options.Policy.AllowFileWrites, Is.False);
		Assert.That(options.ResourceRetentionLimit, Is.GreaterThan(0));
		Assert.That(options.Http.Host, Is.EqualTo("127.0.0.1"));
		Assert.That(options.Http.Port, Is.EqualTo(4153));
		Assert.That(options.Http.Path, Is.EqualTo("/mcp"));
		Assert.That(options.ToolProfile, Is.EqualTo(McpToolProfile.Agent));
	}

	[Test]
	public void CommandLineCanSelectFullToolProfile()
	{
		var options = new McpServerOptions();

		McpCommandLineOptions.Apply(options, ["--tool-profile", "full"]);

		Assert.That(options.ToolProfile, Is.EqualTo(McpToolProfile.Full));
	}

	[Test]
	public void CommandLineCanConfigureContextExpiryAndRejectsUnknownProfile()
	{
		var options = new McpServerOptions();

		McpCommandLineOptions.Apply(options, ["--context-idle-timeout-ms", "2500"]);

		Assert.That(options.ContextIdleTimeoutMs, Is.EqualTo(2500));
		Assert.That(
			() => McpCommandLineOptions.Apply(options, ["--tool-profile", "wide"]),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.InvalidArguments));
	}

	[Test]
	public void CommandLineCanConfigureStartupLaunchAndPolicy()
	{
		var options = new McpServerOptions();

		McpCommandLineOptions.Apply(
			options,
			new[]
			{
				"--allow-launch",
				"--allow-actions",
				"--launch",
				"C:\\apps\\Harness.exe",
				"--launch-args",
				"--smoke",
				"--working-dir",
				"C:\\apps",
				"--terminate-on-detach",
				"--resource-retention-limit",
				"3",
				"--activity-retention-limit",
				"7",
				"--activity-log-file",
				"C:\\temp\\mcp-activity.jsonl",
				"--http-port",
				"0",
				"--http-path",
				"mcp-test",
				"--http-enable-legacy-sse",
				"--endpoint-file",
				"C:\\temp\\endpoint.json",
				"--start-minimized",
			});

		Assert.That(options.Policy.AllowLaunch, Is.True);
		Assert.That(options.Policy.AllowActions, Is.True);
		Assert.That(options.Startup.LaunchPath, Is.EqualTo("C:\\apps\\Harness.exe"));
		Assert.That(options.Startup.LaunchArguments, Is.EqualTo("--smoke"));
		Assert.That(options.Startup.WorkingDirectory, Is.EqualTo("C:\\apps"));
		Assert.That(options.Startup.TerminateOnDetach, Is.True);
		Assert.That(options.ResourceRetentionLimit, Is.EqualTo(3));
		Assert.That(options.ActivityRetentionLimit, Is.EqualTo(7));
		Assert.That(options.ActivityLogFile, Is.EqualTo("C:\\temp\\mcp-activity.jsonl"));
		Assert.That(options.Http.Port, Is.EqualTo(0));
		Assert.That(options.Http.Path, Is.EqualTo("/mcp-test"));
		Assert.That(options.Http.EnableLegacySse, Is.True);
		Assert.That(options.Http.EndpointFile, Is.EqualTo("C:\\temp\\endpoint.json"));
		Assert.That(options.Http.StartMinimized, Is.True);
	}

	[Test]
	public void MissingCommandLineValueMapsToAutomationException()
	{
		Assert.That(
			() => McpCommandLineOptions.Apply(new McpServerOptions(), new[] { "--launch" }),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.InvalidArguments));
	}

	[Test]
	public void InvalidShapeFormatAndSelectorArgumentsMapToAutomationExceptions()
	{
		Assert.That(
			() => McpArgumentParsing.ParseTreeShape("sideways", TreeShape.Flat),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.InvalidArguments));
		Assert.That(
			() => McpArgumentParsing.ParseImageFormat("tiff", ImageFormat.Png),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.InvalidArguments));
		Assert.That(
			() => McpArgumentParsing.ParsePair("missing-separator", "property"),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.InvalidArguments));
	}

	[Test]
	public void LaunchPolicyRejectsExecutablesOutsideAllowedRoots()
	{
		Assert.That(
			() => McpArgumentParsing.ValidateExecutableAllowed("C:\\outside\\Harness.exe", new[] { "C:\\allowed" }),
			Throws.TypeOf<AutomationException>().With.Property("ErrorCode").EqualTo(AutomationErrorCodes.ActionDenied));
	}

	[Test]
	public void GuiSettingsStoreRoundTripsUxOptions()
	{
		var store = new McpGuiSettingsStore(CreateTempSettingsPath());

		store.Save(new McpGuiSettings
		{
			Target = new McpGuiTargetSettings
			{
				AttachPidText = "123",
				AttachProcessName = "Harness",
				AttachWindowTitle = "Harness Window",
				LaunchPath = "C:\\apps\\Harness.exe",
				LaunchArguments = "--smoke",
				TerminateOnDetach = true,
			},
			Policy = new McpGuiPolicySettings
			{
				AllowLaunch = true,
				AllowActions = true,
				AllowArbitraryInvoke = true,
				AllowFileWrites = true,
			},
			VirtualPointer = new McpGuiVirtualPointerSettings
			{
				Enabled = true,
				ShowClickRipples = false,
				ShowDragTrail = false,
				IncludeInScreenshots = true,
				HideDelayMs = "250",
			},
			ActivityFilter = "target",
		});

		var loaded = store.Load();

		Assert.That(loaded.Target.AttachPidText, Is.EqualTo("123"));
		Assert.That(loaded.Target.AttachProcessName, Is.EqualTo("Harness"));
		Assert.That(loaded.Target.AttachWindowTitle, Is.EqualTo("Harness Window"));
		Assert.That(loaded.Target.LaunchPath, Is.EqualTo("C:\\apps\\Harness.exe"));
		Assert.That(loaded.Target.LaunchArguments, Is.EqualTo("--smoke"));
		Assert.That(loaded.Target.TerminateOnDetach, Is.True);
		Assert.That(loaded.Policy.AllowLaunch, Is.True);
		Assert.That(loaded.Policy.AllowActions, Is.True);
		Assert.That(loaded.Policy.AllowArbitraryInvoke, Is.True);
		Assert.That(loaded.Policy.AllowFileWrites, Is.True);
		Assert.That(loaded.VirtualPointer.Enabled, Is.True);
		Assert.That(loaded.VirtualPointer.ShowClickRipples, Is.False);
		Assert.That(loaded.VirtualPointer.ShowDragTrail, Is.False);
		Assert.That(loaded.VirtualPointer.IncludeInScreenshots, Is.True);
		Assert.That(loaded.VirtualPointer.HideDelayMs, Is.EqualTo("250"));
		Assert.That(loaded.ActivityFilter, Is.EqualTo("target"));
	}

	[Test]
	public void GuiSettingsStoreReportsInvalidJsonWithoutThrowingFromTryLoad()
	{
		var path = CreateTempSettingsPath();
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "{bad json");
		var store = new McpGuiSettingsStore(path);

		var loaded = store.TryLoadIfExists(out var settings, out var error);

		Assert.That(loaded, Is.False);
		Assert.That(settings, Is.Null);
		Assert.That(error, Does.Contain("could not be loaded"));
	}

	private static string CreateTempSettingsPath() =>
		Path.Combine(Path.GetTempPath(), "DeepFlowTest.Mcp.Tests", Guid.NewGuid().ToString("N"), "mcp-gui-settings.json");
}
