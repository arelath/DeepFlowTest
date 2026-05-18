namespace DeepFlowTest.Mcp.Tests;

using DeepFlowTest.Cli;
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
			});

		Assert.That(options.Policy.AllowLaunch, Is.True);
		Assert.That(options.Policy.AllowActions, Is.True);
		Assert.That(options.Startup.LaunchPath, Is.EqualTo("C:\\apps\\Harness.exe"));
		Assert.That(options.Startup.LaunchArguments, Is.EqualTo("--smoke"));
		Assert.That(options.Startup.WorkingDirectory, Is.EqualTo("C:\\apps"));
		Assert.That(options.Startup.TerminateOnDetach, Is.True);
		Assert.That(options.ResourceRetentionLimit, Is.EqualTo(3));
	}

	[Test]
	public void MissingCommandLineValueMapsToCliException()
	{
		Assert.That(
			() => McpCommandLineOptions.Apply(new McpServerOptions(), new[] { "--launch" }),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.InvalidArguments));
	}

	[Test]
	public void InvalidShapeFormatAndSelectorArgumentsMapToCliExceptions()
	{
		Assert.That(
			() => McpArgumentParsing.ParseTreeShape("sideways", TreeShape.Flat),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.InvalidArguments));
		Assert.That(
			() => McpArgumentParsing.ParseImageFormat("tiff", ImageFormat.Png),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.InvalidArguments));
		Assert.That(
			() => McpArgumentParsing.ParsePair("missing-separator", "property"),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.InvalidArguments));
	}

	[Test]
	public void LaunchPolicyRejectsExecutablesOutsideAllowedRoots()
	{
		Assert.That(
			() => McpArgumentParsing.ValidateExecutableAllowed("C:\\outside\\Harness.exe", new[] { "C:\\allowed" }),
			Throws.TypeOf<CliException>().With.Property("ErrorCode").EqualTo(CliErrorCodes.ActionDenied));
	}
}
