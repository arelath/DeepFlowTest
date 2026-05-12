namespace DeepFlowTest.Tests;

using System;
using System.Diagnostics;
using System.IO;
using DeepFlowTest.InjectorLauncher;
using NUnit.Framework;

[TestFixture]
public sealed class InjectorLauncherProgramRunTests
{
	[Test]
	public void ProgramRunPrintsHelp()
	{
		var originalOut = Console.Out;
		using var writer = new StringWriter();
		try
		{
			Console.SetOut(writer);

			var exitCode = Program.Run(new[] { "--help" });

			Assert.That(exitCode, Is.EqualTo(InjectorExitCode.Success));
			Assert.That(writer.ToString(), Does.Contain("DeepFlowTest injector launcher"));
		}
		finally
		{
			Console.SetOut(originalOut);
		}
	}

	[Test]
	public void ProgramRunMapsMissingPayload()
	{
		var missingPayload = Path.Combine(Path.GetTempPath(), $"deepflowtest-missing-{Guid.NewGuid():N}.dll");

		var exitCode = Program.Run(CreateArgs(missingPayload));

		Assert.That(exitCode, Is.EqualTo(InjectorExitCode.MissingPayload));
	}

	[Test]
	public void ProgramRunMapsMissingInjectorDllSeparately()
	{
		var payload = Path.Combine(Path.GetTempPath(), $"deepflowtest-payload-{Guid.NewGuid():N}.dll");
		var isolatedRoot = Path.Combine(Path.GetTempPath(), $"deepflowtest-injector-root-{Guid.NewGuid():N}");
		File.WriteAllText(payload, string.Empty);
		Directory.CreateDirectory(isolatedRoot);
		try
		{
			using var _ = InjectorPathResolver.OverrideRootDirectoryForTests(isolatedRoot);

			var exitCode = Program.Run(CreateArgs(payload));

			Assert.That(exitCode, Is.EqualTo(InjectorExitCode.MissingInjectorDll));
		}
		finally
		{
			File.Delete(payload);
			Directory.Delete(isolatedRoot, recursive: true);
		}
	}

	private static string[] CreateArgs(string assemblyPath)
	{
		return new[]
		{
			"--targetPID",
			Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			"--assembly",
			assemblyPath,
			"--className",
			"Payload",
			"--methodName",
			"Start",
		};
	}
}
