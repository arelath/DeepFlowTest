namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using NUnit.Framework;

[TestFixture]
[Category("Integration")]
[Explicit("Starts a real harness process and injects into it.")]
[NonParallelizable]
public sealed class RunningProcessAttachIntegrationTests
{
	private static readonly IReadOnlyList<string> MatcherPropertyNames =
	[
		"Name",
		"AutomationProperties.Name",
		"AutomationProperties.AutomationId",
		"Text",
		"Content",
		"Header",
		"IsChecked",
		"IsEnabled",
		"IsSubmenuOpen",
		"IsVisible",
	];

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

	[Test]
	public void SerializedElementLinqQueriesWithCapturedValuesRunInAttachedHarness()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AppDriver.AttachTo(harness.Process.Id, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		});

		var buttonCriteria = new ElementCriteria("Button", "HelloWorldButton", "Click here");
		var button = driver.GetElement(
			element =>
				element.TypeName == buttonCriteria.TypeName
				&& element["AutomationProperties.AutomationId"] == buttonCriteria.AutomationId
				&& element["Content"] == buttonCriteria.Content,
			timeoutMs: 30_000);

		Assert.That(button.TargetId, Is.Not.Empty);
		Assert.That(button.TypeName, Is.EqualTo("Button"));

		Func<Element, bool> capturedButtonPredicate = element =>
			element.TypeName == "Button"
			&& element["AutomationProperties.AutomationId"] == buttonCriteria.AutomationId
			&& element["Content"] == buttonCriteria.Content;
		var buttonFoundWithCapturedPredicate = driver.GetElement(
			element => capturedButtonPredicate(element),
			timeoutMs: 30_000);

		Assert.That(buttonFoundWithCapturedPredicate.TargetId, Is.EqualTo(button.TargetId));

		var inputAutomationId = "HelloWorldInput";
		var input = driver.GetElement(
			element => element.TypeName == "TextBox" && element["AutomationProperties.AutomationId"] == inputAutomationId,
			timeoutMs: 30_000);
		var updatedText = $"captured-linq-{Guid.NewGuid():N}";

		input.SetProperty<TextBox, string>("Text", _ => updatedText);

		var refreshedInput = driver.GetElement(
			element => element.TypeName == "TextBox" && element["AutomationProperties.AutomationId"] == inputAutomationId,
			timeoutMs: 30_000);
		Assert.That(refreshedInput.GetProperty<string>("Text"), Is.EqualTo(updatedText));
	}

	[Test]
	public void CapturedCompiledElementPredicatesRunAgainstAttachedHarnessSnapshots()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AppDriver.AttachTo(harness.Process.Id, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		});

		var buttonContent = "Click here";
		Func<Element, bool> buttonPredicate = element =>
			element.TypeName == "Button"
			&& element["Content"] == buttonContent
			&& element["IsEnabled"];

		var buttons = driver.GetElements(element => buttonPredicate(element), timeoutMs: 30_000);

		Assert.That(buttons.Select(static element => element["AutomationProperties.AutomationId"].ToString()), Does.Contain("HelloWorldButton"));

		var groupHeader = "Buttons";
		Func<Element, bool> headerPredicate = element =>
			element.TypeName == "GroupBox"
			&& element["Header"] == groupHeader
			&& element["IsEnabled"];

		var group = driver.GetElement(element => headerPredicate(element), timeoutMs: 30_000);

		Assert.That(group["AutomationProperties.AutomationId"].ToString(), Is.EqualTo("ButtonControls"));
	}

	[Test]
	public void SageStyleMenuItemHelperPredicateFindsAttachedHarnessMenuItems()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AppDriver.AttachTo(harness.Process.Id, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		});

		var normalizedHeader = NormalizeMenuHeader("MenuItemOne");
		var menuItem = driver.GetElement(
			element => string.Equals(element.TypeName, "MenuItem", StringComparison.Ordinal)
				&& ElementOrDescendantTextMatches(element, normalizedHeader, 4),
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

		Assert.That(menuItem["AutomationProperties.AutomationId"].ToString(), Is.EqualTo("MenuItemOne"));
		Assert.That(menuItem["Header"].ToString(), Is.EqualTo("MenuItemOne"));
	}

	[Test]
	public void ClickingAttachedHarnessMenuHeaderOpensSubmenu()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AppDriver.AttachTo(harness.Process.Id, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		});

		var header = FindByAutomationId(driver, "MenuHeader");

		header.Click();

		var openedHeader = WaitForMenuHeaderOpen(driver);
		Assert.That(openedHeader.GetProperty<bool>("IsSubmenuOpen"), Is.True);
	}

	[Test]
	public void ClickingAttachedHarnessSubmenuItemRaisesClickAndTogglesCheck()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AppDriver.AttachTo(harness.Process.Id, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		});

		FindByAutomationId(driver, "MenuHeader").Click();
		WaitForMenuHeaderOpen(driver);

		FindByAutomationId(driver, "MenuItemOne").Click();

		WaitForElementText(driver, "HelloWorldInput", "MenuItemOne_Click event triggered.");
		var checkedItem = driver.GetElement(
			element => element["AutomationProperties.AutomationId"] == "MenuItemOne" && element["IsChecked"] == true,
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);
		Assert.That(checkedItem.GetProperty<bool>("IsChecked"), Is.True);
	}

	[Test]
	public void RaisingAttachedHarnessMenuItemClickUsesMenuItemRoutedEvent()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AppDriver.AttachTo(harness.Process.Id, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		});

		FindByAutomationId(driver, "MenuItemTwo").RaiseEvent("Click");

		WaitForElementText(driver, "HelloWorldInput", "MenuItemTwo_Click event triggered.");
	}

	[Test]
	public void RootScopedServerFindsDescendantInAttachedHarness()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AppDriver.AttachTo(harness.Process.Id, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		});

		var buttonGroup = driver.GetElement(
			element => element.TypeName == "GroupBox" && element["Header"] == "Buttons",
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

		var button = driver.GetElement(
			buttonGroup,
			element => element.TypeName == "Button"
				&& element["AutomationProperties.AutomationId"] == "OpenFileDialogButton"
				&& element["Content"] == "Open file dialog",
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

		Assert.That(button.TargetId, Is.Not.Empty);
		Assert.That(button["AutomationProperties.AutomationId"].ToString(), Is.EqualTo("OpenFileDialogButton"));
	}

	[Test]
	public void RootPredicateServerFindsDescendantInAttachedHarnessInOneCommand()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AppDriver.AttachTo(harness.Process.Id, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		});

		var button = driver.GetElement(
			root => root["AutomationProperties.AutomationId"] == "ButtonControls",
			element => element.TypeName == "Button"
				&& element["AutomationProperties.AutomationId"] == "OpenFileDialogButton"
				&& element["Content"] == "Open file dialog",
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

		Assert.That(button.TargetId, Is.Not.Empty);
		Assert.That(button["AutomationProperties.AutomationId"].ToString(), Is.EqualTo("OpenFileDialogButton"));
	}

	[Test]
	public void KeyboardCanTypeShortcutAndNavigateInAttachedHarness()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AppDriver.AttachTo(harness.Process.Id, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		});
		driver.Keyboard.DelayMs = 1;

		var textBox = FindByAutomationId(driver, "TextBox1");
		var typedText = $"keyboard-{Guid.NewGuid():N}";
		driver.Keyboard.Type(textBox, typedText, clearFirst: true);
		WaitForElementText(driver, "TextBox1", typedText);

		var replacementText = $"replacement-{Guid.NewGuid():N}";
		driver.Keyboard.Shortcut(textBox, "Control", "A");
		driver.Keyboard.Type(textBox, replacementText);
		WaitForElementText(driver, "TextBox1", replacementText);

		var checkbox = FindByAutomationId(driver, "MainCheckbox");
		driver.Keyboard.Press(checkbox, "Tab");
		WaitForElementText(driver, "HelloWorldInput", "TextBox1_GotKeyboardFocus event triggered.");
	}

	private static Element AttachAndFind(int processId, string automationId)
	{
		using var driver = AppDriver.AttachTo(processId, new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		});

		Assert.That(driver.Connection.OwnsProcess, Is.False);
		return driver.GetElement(ElementSelector.ByAutomationId(automationId));
	}

	private static Element FindByAutomationId(AppDriver driver, string automationId) =>
		driver.GetElement(
			element => element["AutomationProperties.AutomationId"] == automationId,
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

	private static Element WaitForElementText(AppDriver driver, string automationId, string expectedText) =>
		driver.GetElement(
			element => element["AutomationProperties.AutomationId"] == automationId && element["Text"] == expectedText,
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

	private static Element WaitForMenuHeaderOpen(AppDriver driver) =>
		driver.GetElement(
			element => element["AutomationProperties.AutomationId"] == "MenuHeader" && element["IsSubmenuOpen"] == true,
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

	private sealed record ElementCriteria(string TypeName, string AutomationId, string Content);

	private static string NormalizeMenuHeader(string header) =>
		header.Replace("_", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

	private static bool ElementOrDescendantTextMatches(Element element, string normalizedExpected, int remainingDepth)
	{
		if (ElementTextMatches(element, normalizedExpected))
			return true;

		if (remainingDepth <= 0)
			return false;

		IReadOnlyList<Element> children;
		try
		{
			children = element.Child;
		}
		catch
		{
			return false;
		}

		return children.Any(child => ElementOrDescendantTextMatches(child, normalizedExpected, remainingDepth - 1));
	}

	private static bool ElementTextMatches(Element element, string normalizedExpected)
	{
		foreach (var propertyName in new[] { "Header", "Text", "Content", "AutomationProperties.Name", "Name" })
		{
			if (element.Properties.TryGetValue(propertyName, out var value)
				&& NormalizeMenuHeader(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty) == normalizedExpected)
			{
				return true;
			}
		}

		return false;
	}

	private static string ResolvePayloadRoot()
	{
		var root = Path.Combine(FindRepositoryRoot(), "output");
		var payload = Path.Combine(root, "payloads", "dotnet", "DeepFlowTest.dll");
		Assert.That(File.Exists(payload), Is.True, $"Repacked dotnet payload was not found at '{payload}'. Run '.\\build.ps1 Compile' before integration tests.");
		return root;
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
