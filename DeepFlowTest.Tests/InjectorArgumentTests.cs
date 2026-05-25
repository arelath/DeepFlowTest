namespace DeepFlowTest.Tests;

using System;
using System.IO;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
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
		Assert.That(() => AppDriverPayloadStartupOptions.Decode("not-encoded"), Throws.TypeOf<ProtocolException>());

		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = "pipe",
			Mode = "Other",
			PayloadRoot = "root",
			ProtocolVersion = "1",
		};

		Assert.That(() => options.Encode(), Throws.TypeOf<ProtocolException>());
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
	public void PayloadPathRejectsUnknownFrameworkFamily()
	{
		var root = CreateTempDirectory();
		try
		{
			File.WriteAllText(Path.Combine(root, "DeepFlowTest.dll"), string.Empty);

			Assert.That(
				() => InjectorPathResolver.ResolvePayloadPath(root, "unknown-runtime"),
				Throws.TypeOf<InjectorLauncherException>().With.Property(nameof(InjectorLauncherException.ExitCode)).EqualTo(InjectorExitCode.UnsupportedTarget));
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
	public void ResourcePathUsesArchitectureFolderAsRootWithoutDuplicatingResourceSegments()
	{
		var root = CreateTempDirectory();
		try
		{
			var resourceRoot = Path.Combine(root, "DeepFlowTestResources", "x64");
			Directory.CreateDirectory(resourceRoot);

			var path = InjectorPathResolver.ResolveResourcePath(resourceRoot, "x64", "DeepFlowTest.GenericInjector.x64.dll");

			Assert.That(path, Is.EqualTo(Path.Combine(resourceRoot, "DeepFlowTest.GenericInjector.x64.dll")));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public void ResourcePathUsesSiblingNativeDllWhenLauncherRunsFromArchitectureFolder()
	{
		var root = CreateTempDirectory();
		try
		{
			var resourceRoot = Path.Combine(root, "DeepFlowTestResources", "x64");
			var nativeDll = Path.Combine(resourceRoot, "DeepFlowTest.GenericInjector.x64.dll");
			Directory.CreateDirectory(resourceRoot);
			File.WriteAllText(nativeDll, string.Empty);

			var path = InjectorPathResolver.ResolveResourcePath(resourceRoot, "x64", "DeepFlowTest.GenericInjector.x64.dll");

			Assert.That(path, Is.EqualTo(nativeDll));
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

	[Test]
	public void InjectorArgumentsEscapePayloadRootTrailingBackslash()
	{
		using var connection = AppConnection.ForAttach(new Fakes.FakeTargetProcess { Id = 123 }, "pipe-123");

		var arguments = ExternalInjectorAppConnectionInjector.BuildInjectorArguments(
			connection,
			"dft:abc",
			@"C:\payload root\");

		Assert.That(arguments, Does.Contain(@"""C:\payload root\\"""));
		Assert.That(arguments, Does.Not.Contain(@"""C:\payload root\"""));
	}

	private static string CreateTempDirectory()
	{
		var root = Path.Combine(Path.GetTempPath(), $"deepflowtest-injector-test-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		return root;
	}

}
