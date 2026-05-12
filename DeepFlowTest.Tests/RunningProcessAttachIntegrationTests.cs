namespace DeepFlowTest.Tests;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;

[TestFixture]
[Category("Integration")]
[Explicit("Starts a real harness process and injects into it.")]
[NonParallelizable]
public sealed class RunningProcessAttachIntegrationTests
{
	[Test]
	public void AttachToRunningProcessCanDisconnectAndReattach()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());

		var window = AttachAndFind(harness.Process.Id, "HelloWorldWindow");
		Assert.That(window.TypeName, Is.EqualTo("MainWindow"));
		Assert.That(harness.Process.HasExited, Is.False, "The first attached driver must not own or stop the harness process.");

		var button = AttachAndFind(harness.Process.Id, "HelloWorldButton");
		Assert.That(button.TypeName, Is.EqualTo("Button"));
		Assert.That(harness.Process.HasExited, Is.False, "The second attached driver must not own or stop the harness process.");
	}

	private static Element AttachAndFind(int processId, string automationId)
	{
		using var driver = AppDriver.AttachTo(processId, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
		});

		Assert.That(driver.Connection.OwnsProcess, Is.False);
		return driver.GetElement(ElementSelector.ByAutomationId(automationId));
	}

	private static string ResolveHelloWorldExecutablePath()
	{
		var path = Path.Combine(
			FindRepositoryRoot(),
			"TestHarnesses",
			"bin",
			"HelloWorld",
			"Debug",
			"net8.0-windows",
			"HelloWorld.exe");

		Assert.That(File.Exists(path), Is.True, $"HelloWorld harness was not found at '{path}'. Build CompileTestHarnesses first.");
		return path;
	}

	private static string FindRepositoryRoot()
	{
		var directory = TestContext.CurrentContext.TestDirectory;
		while (!string.IsNullOrWhiteSpace(directory))
		{
			if (File.Exists(Path.Combine(directory, "DeepFlowTest.sln")))
				return directory;

			directory = Directory.GetParent(directory)?.FullName;
		}

		throw new DirectoryNotFoundException("Could not find the repository root.");
	}

	private sealed class HarnessProcess : IDisposable
	{
		private HarnessProcess(Process process)
		{
			Process = process;
		}

		public Process Process { get; }

		public static HarnessProcess Start(string executablePath)
		{
			var process = Process.Start(new ProcessStartInfo(executablePath)
			{
				UseShellExecute = false,
				WorkingDirectory = Path.GetDirectoryName(executablePath)!,
			}) ?? throw new InvalidOperationException($"Failed to start '{executablePath}'.");

			try
			{
				WaitForMainWindow(process, TimeSpan.FromSeconds(15));
				return new HarnessProcess(process);
			}
			catch
			{
				Stop(process);
				process.Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			Stop(Process);
			Process.Dispose();
		}

		private static void WaitForMainWindow(Process process, TimeSpan timeout)
		{
			var stopwatch = Stopwatch.StartNew();
			while (stopwatch.Elapsed < timeout)
			{
				if (process.HasExited)
					throw new InvalidOperationException($"Harness process exited with code {process.ExitCode} before creating a main window.");

				process.Refresh();
				if (process.MainWindowHandle != IntPtr.Zero)
					return;

				Thread.Sleep(100);
			}

			throw new TimeoutException($"Harness process did not create a main window within {timeout.TotalSeconds:0} seconds.");
		}

		private static void Stop(Process process)
		{
			if (process.HasExited)
				return;

			try
			{
				process.CloseMainWindow();
				if (process.WaitForExit(5_000))
					return;
			}
			catch (InvalidOperationException)
			{
				return;
			}

			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
	}
}
