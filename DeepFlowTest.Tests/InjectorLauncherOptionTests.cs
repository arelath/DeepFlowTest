namespace DeepFlowTest.Tests;

using DeepFlowTest.InjectorLauncher;
using NUnit.Framework;

[TestFixture]
public sealed class InjectorLauncherOptionTests
{
	[Test]
	public void ParsesRequiredAndOptionalOptions()
	{
		var ok = InjectorLauncherCommandLineOptions.TryParse(
			new[]
			{
				"--targetPID", "123",
				"--targetHwnd", "456",
				"--assembly", "DeepFlowTest",
				"--className", "Payload",
				"--methodName", "Start",
				"--startupArgument", "dft:value",
				"--verbose",
				"--debug",
				"--attachConsoleToParent",
			},
			out var options,
			out var error);

		Assert.That(ok, Is.True, error);
		Assert.That(options.TargetProcessId, Is.EqualTo(123));
		Assert.That(options.TargetWindowHandle, Is.EqualTo(456));
		Assert.That(options.Assembly, Is.EqualTo("DeepFlowTest"));
		Assert.That(options.ClassName, Is.EqualTo("Payload"));
		Assert.That(options.MethodName, Is.EqualTo("Start"));
		Assert.That(options.StartupArgument, Is.EqualTo("dft:value"));
		Assert.That(options.Verbose, Is.True);
		Assert.That(options.Debug, Is.True);
		Assert.That(options.AttachConsoleToParent, Is.True);
	}

	[Test]
	public void MissingRequiredOptionFailsWithoutThrowing()
	{
		var ok = InjectorLauncherCommandLineOptions.TryParse(
			new[] { "--targetPID", "123", "--assembly", "DeepFlowTest", "--methodName", "Start" },
			out _,
			out var error);

		Assert.That(ok, Is.False);
		Assert.That(error, Does.Contain("className"));
	}

	[Test]
	public void ParsesHwndOnlyTargetSelector()
	{
		var ok = InjectorLauncherCommandLineOptions.TryParse(
			new[]
			{
				"--targetHwnd", "456",
				"--assembly", "DeepFlowTest",
				"--className", "Payload",
				"--methodName", "Start",
			},
			out var options,
			out var error);

		Assert.That(ok, Is.True, error);
		Assert.That(options.HasTargetProcessId, Is.False);
		Assert.That(options.HasTargetWindowHandle, Is.True);
		Assert.That(options.TargetWindowHandle, Is.EqualTo(456));
	}

	[Test]
	public void ParsesHelpWithoutRequiredTargetOptions()
	{
		var ok = InjectorLauncherCommandLineOptions.TryParse(new[] { "--help" }, out var options, out var error);

		Assert.That(ok, Is.True, error);
		Assert.That(options.HelpRequested, Is.True);
		Assert.That(InjectorLauncherCommandLineOptions.HelpText, Does.Contain("DeepFlowTest injector launcher"));
	}
}
