namespace DeepFlowTest.Cli.Tests;

using System.CommandLine;
using System.Linq;
using NUnit.Framework;

[TestFixture]
public sealed class CommandParsingTests
{
	[TestCase("config get")]
	[TestCase("config set timeoutMs 1500")]
	[TestCase("config clear timeoutMs")]
	[TestCase("config reset")]
	[TestCase("processes")]
	[TestCase("processes --show-all")]
	[TestCase("ping --pid 1")]
	[TestCase("pipe status --pid 1")]
	[TestCase("tree --pid 1")]
	[TestCase("find --pid 1 --name Main")]
	[TestCase("find --pid 1 --prop Name=Main")]
	[TestCase("find --pid 1 --contains Text=Mai")]
	[TestCase("find --pid 1 --regex Name=^Main$")]
	[TestCase("node --pid 1 --target dft-1")]
	[TestCase("props --pid 1 --target dft-1")]
	[TestCase("selectors --pid 1 --target dft-1")]
	[TestCase("screenshot --pid 1 --target dft-1")]
	[TestCase("screenshot --pid 1 --out capture.png")]
	[TestCase("wait --pid 1 --name Ready")]
	[TestCase("stream visual-tree --pid 1")]
	[TestCase("stream visual-tree-delta --pid 1")]
	[TestCase("stream screenshot --pid 1")]
	[TestCase("stream event-log --pid 1")]
	[TestCase("click --pid 1 --target dft-1")]
	[TestCase("click --pid 1 --target dft-1 --double")]
	[TestCase("click --pid 1 --prop Name=Save --require-visible")]
	[TestCase("focus --pid 1 --target dft-1")]
	[TestCase("type --pid 1 --target dft-1 --text hello")]
	[TestCase("key --pid 1 --keys Enter")]
	[TestCase("set --pid 1 --target dft-1 --property Text --value hello")]
	[TestCase("raise --pid 1 --target dft-1 --event Click")]
	[TestCase("invoke --pid 1 --target dft-1 --code payload")]
	[TestCase("version")]
	public void ParserRecognizesCommandSurface(string commandLine)
	{
		var parse = Program.CreateRootCommand().Parse(Split(commandLine), new ParserConfiguration());

		Assert.That(parse.Errors, Is.Empty);
	}

	[Test]
	public void ParserRejectsUnknownCommand()
	{
		var parse = Program.CreateRootCommand().Parse(new[] { "unknown-command" }, new ParserConfiguration());

		Assert.That(parse.Errors, Is.Not.Empty);
	}

	[Test]
	public void RegisteredLaterCommandReturnsStableNotImplementedEnvelope()
	{
		var result = CliTestHost.Run(new[] { "config" });

		Assert.That(result.ExitCode, Is.EqualTo(1));
		Assert.That(result.Stdout, Does.Contain("\"code\":\"not-implemented\""));
	}

	[Test]
	public void NoArgumentsShowsHelpSuccessfully()
	{
		var result = CliTestHost.Run(System.Array.Empty<string>());

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("DeepFlowTest CLI"));
	}

	[Test]
	public void VersionDefaultsToJsonEnvelope()
	{
		var result = CliTestHost.Run(new[] { "version" });

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"ok\":true"));
		Assert.That(result.Stdout, Does.Contain("\"productName\":\"DeepFlowTest\""));
	}

	private static string[] Split(string commandLine) =>
		commandLine.Split(' ').Where(static token => token.Length != 0).ToArray();
}
