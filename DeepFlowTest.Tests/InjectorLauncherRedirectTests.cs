namespace DeepFlowTest.Tests;

using System.IO;
using DeepFlowTest.InjectorLauncher;
using NUnit.Framework;

[TestFixture]
public sealed class InjectorLauncherRedirectTests
{
	[Test]
	public void RedirectPathUsesTargetArchitectureExecutableName()
	{
		var path = ArchitectureRedirect.GetLauncherPath(@"C:\tools\DeepFlowTest.InjectorLauncher.x86.exe", "x64");

		Assert.That(path, Is.EqualTo(@"C:\tools\DeepFlowTest.InjectorLauncher.x64.exe"));
	}

	[Test]
	public void RedirectCommandPreservesOriginalArguments()
	{
		var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		Directory.CreateDirectory(root);
		try
		{
			var currentExe = Path.Combine(root, "DeepFlowTest.InjectorLauncher.x86.exe");
			var targetExe = Path.Combine(root, "DeepFlowTest.InjectorLauncher.x64.exe");
			File.WriteAllText(targetExe, string.Empty);

			var startInfo = ArchitectureRedirect.CreateStartInfo(currentExe, "x86", "x64", new[] { "--assembly", @"C:\Program Files\DeepFlowTest.dll" });

			Assert.That(startInfo, Is.Not.Null);
			Assert.That(startInfo!.FileName, Is.EqualTo(targetExe));
			Assert.That(startInfo.Arguments, Is.EqualTo("--assembly \"C:\\Program Files\\DeepFlowTest.dll\""));
			Assert.That(startInfo.CreateNoWindow, Is.True);
			Assert.That(startInfo.UseShellExecute, Is.False);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public void MissingRedirectExecutableReturnsControlledFailure()
	{
		Assert.That(
			() => ArchitectureRedirect.CreateStartInfo(@"C:\tools\DeepFlowTest.InjectorLauncher.x86.exe", "x86", "x64", System.Array.Empty<string>()),
			Throws.TypeOf<InjectorLauncherException>().With.Property(nameof(InjectorLauncherException.ExitCode)).EqualTo(InjectorExitCode.MissingArchitectureLauncher));
	}

	[Test]
	public void RedirectRunPassesThroughExitCode()
	{
		var startInfo = new System.Diagnostics.ProcessStartInfo("DeepFlowTest.InjectorLauncher.x64.exe");

		var exitCode = ArchitectureRedirect.Run(startInfo, _ => new FakeRedirectedProcess(37));

		Assert.That(exitCode, Is.EqualTo(37));
	}

	private sealed class FakeRedirectedProcess : IRedirectedProcess
	{
		public FakeRedirectedProcess(int exitCode)
		{
			ExitCode = exitCode;
		}

		public int ExitCode { get; }

		public void WaitForExit()
		{
		}

		public void Dispose()
		{
		}
	}
}
