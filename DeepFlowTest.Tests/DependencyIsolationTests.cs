namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;
using WinForms = System.Windows.Forms;

[TestFixture]
public sealed class DependencyIsolationTests
{
	[Test]
	public void BuildScriptUsesILRepackForPayloads()
	{
		var root = FindRepositoryRoot();
		var buildScript = File.ReadAllText(Path.Combine(root, ".build", "Build.cs"));
		var repackProject = File.ReadAllText(Path.Combine(root, ".build", "PayloadRepack.proj"));

		Assert.That(buildScript, Does.Contain("RunILRepack"));
		Assert.That(repackProject, Does.Contain("TaskName=\"ILRepack\""));
		Assert.That(repackProject, Does.Contain("Internalize=\"true\""));
	}

	[Test]
	public void RepackedPayloadFoldersContainOnlyProductPayloadFiles()
	{
		var root = FindRepositoryRoot();
		var payloadRoot = Path.Combine(root, "output", "payloads");
		Assert.That(Directory.Exists(payloadRoot), Is.True, "Repacked payload root should exist. Run .\\build.ps1 Compile before this test lane.");

		foreach (var family in new[] { "netframework", "netcoreapp", "dotnet" })
		{
			var folder = Path.Combine(payloadRoot, family);
			Assert.That(File.Exists(Path.Combine(folder, "DeepFlowTest.dll")), Is.True, $"{family} payload should exist.");
			Assert.That(File.Exists(Path.Combine(folder, "DeepFlowTest.payload.md")), Is.True, $"{family} manifest should exist.");

			var looseThirdPartyDlls = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly)
				.Select(Path.GetFileName)
				.Where(name => ThirdPartyPayloadDlls.Contains(name!))
				.ToArray();
			Assert.That(looseThirdPartyDlls, Is.Empty, $"{family} should not publish loose third-party payload DLLs.");
		}
	}

	[Test]
	public void RepackedDotnetPayloadDoesNotReferenceLooseThirdPartyAssemblies()
	{
		var root = FindRepositoryRoot();
		var payloadPath = Path.Combine(root, "output", "payloads", "dotnet", "DeepFlowTest.dll");
		Assert.That(File.Exists(payloadPath), Is.True, "Repacked dotnet payload should exist. Run .\\build.ps1 Compile before this test lane.");

		var references = Assembly.LoadFile(payloadPath)
			.GetReferencedAssemblies()
			.Select(name => $"{name.Name}.dll")
			.ToHashSet(System.StringComparer.OrdinalIgnoreCase);

		Assert.That(references.Contains("Newtonsoft.Json.dll"), Is.False);
		Assert.That(references.Contains("Serialize.Linq.dll"), Is.False);
		Assert.That(references.Contains("0Harmony.dll"), Is.False);
	}

	[Test]
	public async Task RepackedPayloadCanAnswerHelloWithNewtonsoftAlreadyLoaded()
	{
		var root = FindRepositoryRoot();
		var payloadPath = Path.Combine(root, "output", "payloads", "dotnet", "DeepFlowTest.dll");
		Assert.That(File.Exists(payloadPath), Is.True, "Repacked dotnet payload should exist. Run .\\build.ps1 Compile before this test lane.");

		Assert.That(typeof(Newtonsoft.Json.JsonConvert).Assembly.GetName().Name, Is.EqualTo("Newtonsoft.Json"));

		var pipeName = $"deepflowtest-conflict-{Guid.NewGuid():N}";
		var startupArgument = new AppDriverPayloadStartupOptions
		{
			PipeName = pipeName,
			Mode = PayloadStartupModes.OneShotDriver,
			PayloadRoot = Path.GetDirectoryName(payloadPath)!,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		}.Encode();

		var loadContext = new PayloadLoadContext(payloadPath);
		var payloadAssembly = loadContext.LoadFromAssemblyPath(payloadPath);
		var startMethod = payloadAssembly
			.GetType("DeepFlowTest.AppDriverPayload.AppDriverPayload", throwOnError: true)!
			.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)!;

		Exception? startupException = null;
		int? startupExitCode = null;
		WinForms.Form? form = null;
		using var started = new ManualResetEventSlim();
		var uiThread = new Thread(() =>
		{
			try
			{
				form = new WinForms.Form { Text = "DeepFlowTest Dependency Conflict Harness" };
				form.Show();
				startupExitCode = (int)startMethod.Invoke(null, new object[] { startupArgument })!;
				started.Set();
				WinForms.Application.Run(form);
			}
			catch (Exception ex)
			{
				startupException = ex;
				started.Set();
			}
		});
		uiThread.SetApartmentState(ApartmentState.STA);
		uiThread.IsBackground = true;
		uiThread.Start();

		try
		{
			Assert.That(started.Wait(TimeSpan.FromSeconds(5)), Is.True);
			Assert.That(startupException, Is.Null);
			Assert.That(startupExitCode, Is.EqualTo(0));

			using var client = new NamedPipeClient(pipeName, connectTimeoutMs: 1000, connectRetryCount: 5);
			var response = await client.SendAsync(new HelloCommandRequest(), responseTimeoutMs: 5000);
			var hello = MessagePacker.ConvertTo<HelloCommandResponse>(response);

			Assert.That(hello.PipeName, Is.EqualTo(pipeName));
			Assert.That(hello.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));
		}
		finally
		{
			if (form is not null && form.IsHandleCreated)
				form.BeginInvoke(new Action(() => form.Close()));
			uiThread.Join(TimeSpan.FromSeconds(5));
			loadContext.Unload();
		}
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
			directory = directory.Parent;

		return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
	}

	private static readonly HashSet<string> ThirdPartyPayloadDlls = new(System.StringComparer.OrdinalIgnoreCase)
	{
		"0Harmony.dll",
		"Newtonsoft.Json.dll",
		"Serialize.Linq.dll",
		"System.Buffers.dll",
		"System.Memory.dll",
		"System.Numerics.Vectors.dll",
		"System.Runtime.CompilerServices.Unsafe.dll",
		"System.ValueTuple.dll",
	};

	private sealed class PayloadLoadContext : AssemblyLoadContext
	{
		private readonly string payloadPath;

		public PayloadLoadContext(string payloadPath)
			: base(isCollectible: true)
		{
			this.payloadPath = payloadPath;
		}

		protected override Assembly? Load(AssemblyName assemblyName)
		{
			if (assemblyName.Name == "DeepFlowTest")
				return LoadFromAssemblyPath(payloadPath);

			return null;
		}
	}
}
