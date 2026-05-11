namespace DeepFlowTest.Tests;

using System;
using System.IO;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.InjectorLauncher;
using DeepFlowTest.Shared;
using NUnit.Framework;

[TestFixture]
public sealed class InjectorArgumentTests
{
	[Test]
	public void StartupOptionsRoundTripWithoutDelimiterAmbiguity()
	{
		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = "deepflowtest-pipe",
			Mode = PayloadStartupModes.ReusableCli,
			PayloadRoot = @"C:\payload root\with spaces",
			ProtocolVersion = "1",
		};

		var encoded = options.Encode();
		var decoded = AppDriverPayloadStartupOptions.Decode(encoded);

		Assert.That(encoded, Does.Not.Contain("<|>"));
		Assert.That(decoded.PipeName, Is.EqualTo(options.PipeName));
		Assert.That(decoded.Mode, Is.EqualTo(options.Mode));
		Assert.That(decoded.PayloadRoot, Is.EqualTo(options.PayloadRoot));
		Assert.That(decoded.ProtocolVersion, Is.EqualTo(options.ProtocolVersion));
	}

	[Test]
	public void StartupOptionsRejectMalformedAndUnknownMode()
	{
		Assert.That(() => AppDriverPayloadStartupOptions.Decode("not-encoded"), Throws.ArgumentException);

		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = "pipe",
			Mode = "Other",
			PayloadRoot = "root",
			ProtocolVersion = "1",
		};

		Assert.That(() => options.Encode(), Throws.InvalidOperationException);
	}

	[Test]
	public void PayloadPathPrefersFrameworkSpecificPayload()
	{
		var root = CreateTempDirectory();
		try
		{
			var frameworkPayload = Path.Combine(root, "payloads", "netframework", "DeepFlowTest.dll");
			Directory.CreateDirectory(Path.GetDirectoryName(frameworkPayload)!);
			File.WriteAllText(frameworkPayload, string.Empty);
			File.WriteAllText(Path.Combine(root, "DeepFlowTest.dll"), string.Empty);

			Assert.That(InjectorPathResolver.ResolvePayloadPath(root, "netframework"), Is.EqualTo(frameworkPayload));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public void PayloadPathResolvesRequestedAssemblyName()
	{
		var root = CreateTempDirectory();
		try
		{
			var frameworkPayload = Path.Combine(root, "payloads", "dotnet", "CustomPayload.dll");
			Directory.CreateDirectory(Path.GetDirectoryName(frameworkPayload)!);
			File.WriteAllText(frameworkPayload, string.Empty);

			Assert.That(InjectorPathResolver.ResolvePayloadPath(root, "dotnet", "CustomPayload"), Is.EqualTo(frameworkPayload));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public void PayloadPathFallsBackToRootPayload()
	{
		var root = CreateTempDirectory();
		try
		{
			var rootPayload = Path.Combine(root, "DeepFlowTest.dll");
			File.WriteAllText(rootPayload, string.Empty);

			Assert.That(InjectorPathResolver.ResolvePayloadPath(root, "netframework"), Is.EqualTo(rootPayload));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public void PayloadPathFailsWhenPayloadIsMissing()
	{
		var root = CreateTempDirectory();
		try
		{
			Assert.That(() => InjectorPathResolver.ResolvePayloadPath(root, "dotnet"), Throws.TypeOf<FileNotFoundException>());
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public void NativeParameterContainsExpectedParts()
	{
		var data = new InjectorData
		{
			FullAssemblyPath = @"C:\payload\DeepFlowTest.dll",
			ClassName = "DeepFlowTest.AppDriverPayload.AppDriverPayload",
			MethodName = "Start",
			StartupArgument = "dft:abc",
		};
		var paths = new InjectorDllPaths("DeepFlowTest.GenericInjector.x86.dll", @"C:\resources\DeepFlowTest.GenericInjector.x86.dll", data.FullAssemblyPath);

		var invocation = Injector.BuildInvocation("dotnet", data, paths, @"C:\logs\native.log");

		var parts = invocation.NativeParameter.Split(new[] { "<|>" }, StringSplitOptions.None);
		Assert.That(parts, Has.Length.EqualTo(6));
		Assert.That(parts[0], Is.EqualTo("dotnet"));
		Assert.That(parts[1], Is.EqualTo(data.FullAssemblyPath));
		Assert.That(parts[2], Is.EqualTo(data.ClassName));
		Assert.That(parts[3], Is.EqualTo(data.MethodName));
		Assert.That(parts[4], Is.EqualTo(data.StartupArgument));
	}

	private static string CreateTempDirectory()
	{
		var root = Path.Combine(Path.GetTempPath(), $"deepflowtest-injector-test-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		return root;
	}

}
