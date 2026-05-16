namespace DeepFlowTest.Tests;

using System.Diagnostics;
using DeepFlowTest.InjectorLauncher;
using DeepFlowTest.Shared;
using NUnit.Framework;

[TestFixture]
public sealed class ArchitectureAndFrameworkTests
{
	[Test]
	public void ArchitectureNormalizationUsesStableNames()
	{
		Assert.That(ArchitectureDetector.Normalize("Win32"), Is.EqualTo("x86"));
		Assert.That(ArchitectureDetector.Normalize("amd64"), Is.EqualTo("x64"));
		Assert.That(ArchitectureDetector.Normalize("ARM64"), Is.EqualTo("ARM64"));
	}

	[Test]
	public void UnsupportedArchitectureIsRejected()
	{
		Assert.That(ArchitectureDetector.IsSupported("x86"), Is.True);
		Assert.That(ArchitectureDetector.IsSupported("x64"), Is.True);
		Assert.That(ArchitectureDetector.IsSupported("ARM64"), Is.False);
		Assert.That(
			() => ((NativeMethods.ImageFileMachine)0x9999).ToStableName(),
			Throws.TypeOf<InjectorLauncherException>().With.Property(nameof(InjectorLauncherException.ExitCode)).EqualTo(InjectorExitCode.UnsupportedTarget));
	}

	[Test]
	public void FrameworkClassificationUsesModuleEvidence()
	{
		Assert.That(FrameworkDetector.Classify(new[] { new ModuleEvidence("PresentationFramework.dll", productVersion: "4.8.9032.0") }), Is.EqualTo("netframework"));
		Assert.That(FrameworkDetector.Classify(new[] { new ModuleEvidence("clr.dll", productVersion: "4.8.9032.0") }), Is.EqualTo("netframework"));
		Assert.That(FrameworkDetector.Classify(new[] { new ModuleEvidence("mscorlib.ni.dll", productVersion: "4.8.9032.0") }), Is.EqualTo("netframework"));
		Assert.That(FrameworkDetector.Classify(new[] { new ModuleEvidence("coreclr.dll", productVersion: "3.1.32") }), Is.EqualTo("netcoreapp"));
		Assert.That(FrameworkDetector.Classify(new[] { new ModuleEvidence("System.Runtime.dll", productVersion: "8.0.21") }), Is.EqualTo("dotnet"));
	}

	[Test]
	public void FrameworkClassificationRejectsNoRuntimeEvidence()
	{
		Assert.That(
			() => FrameworkDetector.Classify(new[] { new ModuleEvidence("kernel32.dll", productVersion: "10.0.0") }),
			Throws.TypeOf<InjectorLauncherException>().With.Property(nameof(InjectorLauncherException.ExitCode)).EqualTo(InjectorExitCode.UnsupportedTarget));
	}

	[Test]
	public void FrameworkClassificationPrefersCoreRuntimeEvidence()
	{
		var modules = new[]
		{
			new ModuleEvidence("PresentationFramework.dll", productVersion: "4.8.9032.0"),
			new ModuleEvidence("coreclr.dll", productVersion: "8.0.21"),
		};

		Assert.That(FrameworkDetector.Classify(modules), Is.EqualTo("dotnet"));
	}

	[Test]
	public void CurrentProcessResolutionSucceeds()
	{
		using var wrapper = ProcessWrapper.From(Process.GetCurrentProcess().Id, System.IntPtr.Zero);

		Assert.That(wrapper, Is.Not.Null);
		Assert.That(wrapper!.Id, Is.EqualTo(Process.GetCurrentProcess().Id));
		Assert.That(wrapper.Architecture, Is.AnyOf("x86", "x64"));
		Assert.That(wrapper.SupportedFrameworkFamily, Is.AnyOf("netframework", "netcoreapp", "dotnet"));
	}

	[Test]
	public void MissingProcessResolutionReturnsNull()
	{
		Assert.That(ProcessWrapper.From(int.MaxValue, System.IntPtr.Zero), Is.Null);
	}
}
