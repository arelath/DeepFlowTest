namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using DeepFlowTest.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

[TestFixture]
[Category("Integration")]
[Explicit("Starts a real harness process and injects into it.")]
[NonParallelizable]
public sealed class RunningProcessAttachIntegrationTests
{
	private static readonly IReadOnlyList<string> MatcherPropertyNames =
	[
		KnownProperties.Name,
		KnownProperties.AutomationName,
		KnownProperties.AutomationId,
		KnownProperties.Text,
		KnownProperties.Content,
		KnownProperties.Header,
		KnownProperties.IsChecked,
		KnownProperties.IsEnabled,
		KnownProperties.IsSubmenuOpen,
		KnownProperties.IsVisible,
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
		using var driver = AttachToHarness(harness.Process.Id, nameof(SerializedElementLinqQueriesWithCapturedValuesRunInAttachedHarness));

		var buttonCriteria = new ElementCriteria("Button", "HelloWorldButton", "Click here");
		var button = driver.GetElement(
			element =>
				element.TypeName == buttonCriteria.TypeName
				&& element[KnownProperties.AutomationId] == buttonCriteria.AutomationId
				&& element[KnownProperties.Content] == buttonCriteria.Content,
			timeoutMs: 30_000);

		Assert.That(button.TargetId, Is.Not.Empty);
		Assert.That(button.TypeName, Is.EqualTo("Button"));

		Func<Element, bool> capturedButtonPredicate = element =>
			element.TypeName == "Button"
			&& element[KnownProperties.AutomationId] == buttonCriteria.AutomationId
			&& element[KnownProperties.Content] == buttonCriteria.Content;
		var buttonFoundWithCapturedPredicate = driver.GetElement(
			element => capturedButtonPredicate(element),
			timeoutMs: 30_000);

		Assert.That(buttonFoundWithCapturedPredicate.TargetId, Is.EqualTo(button.TargetId));

		var inputAutomationId = "HelloWorldInput";
		var input = driver.GetElement(
			element => element.TypeName == "TextBox" && element[KnownProperties.AutomationId] == inputAutomationId,
			timeoutMs: 30_000);
		var updatedText = $"captured-linq-{Guid.NewGuid():N}";

		input.SetProperty<TextBox, string>(KnownProperties.Text, _ => updatedText);

		var refreshedInput = driver.GetElement(
			element => element.TypeName == "TextBox" && element[KnownProperties.AutomationId] == inputAutomationId,
			timeoutMs: 30_000);
		Assert.That(refreshedInput.GetProperty<string>(KnownProperties.Text), Is.EqualTo(updatedText));
	}

	[Test]
	public void CapturedCompiledElementPredicatesRunAgainstAttachedHarnessSnapshots()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AttachToHarness(harness.Process.Id, nameof(CapturedCompiledElementPredicatesRunAgainstAttachedHarnessSnapshots));

		var buttonContent = "Click here";
		Func<Element, bool> buttonPredicate = element =>
			element.TypeName == "Button"
			&& element[KnownProperties.Content] == buttonContent
			&& element[KnownProperties.IsEnabled];

		var buttons = driver.GetElements(element => buttonPredicate(element), timeoutMs: 30_000);

		Assert.That(buttons.Select(static element => element[KnownProperties.AutomationId].ToString()), Does.Contain("HelloWorldButton"));

		var groupHeader = "Buttons";
		Func<Element, bool> headerPredicate = element =>
			element.TypeName == "GroupBox"
			&& element[KnownProperties.Header] == groupHeader
			&& element[KnownProperties.IsEnabled];

		var group = driver.GetElement(element => headerPredicate(element), timeoutMs: 30_000);

		Assert.That(group[KnownProperties.AutomationId].ToString(), Is.EqualTo("ButtonControls"));
	}

	[Test]
	public void SageStyleMenuItemHelperPredicateFindsAttachedHarnessMenuItems()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AttachToHarness(harness.Process.Id, nameof(SageStyleMenuItemHelperPredicateFindsAttachedHarnessMenuItems));

		var normalizedHeader = NormalizeMenuHeader("MenuItemOne");
		var menuItem = driver.GetElement(
			element => string.Equals(element.TypeName, "MenuItem", StringComparison.Ordinal)
				&& ElementOrDescendantTextMatches(element, normalizedHeader, 4),
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

		Assert.That(menuItem[KnownProperties.AutomationId].ToString(), Is.EqualTo("MenuItemOne"));
		Assert.That(menuItem[KnownProperties.Header].ToString(), Is.EqualTo("MenuItemOne"));
	}

	[Test]
	public void ClickingAttachedHarnessMenuHeaderOpensSubmenu()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AttachToHarness(harness.Process.Id, nameof(ClickingAttachedHarnessMenuHeaderOpensSubmenu));

		var header = FindByAutomationId(driver, "MenuHeader");

		header.Click();

		var openedHeader = WaitForMenuHeaderOpen(driver);
		Assert.That(openedHeader.GetProperty<bool>(KnownProperties.IsSubmenuOpen), Is.True);
	}

	[Test]
	public void ClickingAttachedHarnessSubmenuItemRaisesClickAndTogglesCheck()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AttachToHarness(harness.Process.Id, nameof(ClickingAttachedHarnessSubmenuItemRaisesClickAndTogglesCheck));

		FindByAutomationId(driver, "MenuHeader").Click();
		WaitForMenuHeaderOpen(driver);

		FindByAutomationId(driver, "MenuItemOne").Click();

		WaitForElementText(driver, "HelloWorldInput", "MenuItemOne_Click event triggered.");
		var checkedItem = driver.GetElement(
			element => element[KnownProperties.AutomationId] == "MenuItemOne" && element[KnownProperties.IsChecked] == true,
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);
		Assert.That(checkedItem.GetProperty<bool>(KnownProperties.IsChecked), Is.True);
	}

	[Test]
	public void RaisingAttachedHarnessMenuItemClickUsesMenuItemRoutedEvent()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AttachToHarness(harness.Process.Id, nameof(RaisingAttachedHarnessMenuItemClickUsesMenuItemRoutedEvent));

		FindByAutomationId(driver, "MenuItemTwo").RaiseEvent("Click");

		WaitForElementText(driver, "HelloWorldInput", "MenuItemTwo_Click event triggered.");
	}

	[Test]
	public void DoubleClickingAttachedHarnessButtonRaisesMouseDoubleClick()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AttachToHarness(harness.Process.Id, nameof(DoubleClickingAttachedHarnessButtonRaisesMouseDoubleClick));

		FindByAutomationId(driver, "HelloWorldButton").DoubleClick();

		WaitForElementText(driver, "HelloWorldInput", "HelloWorldButton_DoubleClick event triggered.");
	}

	[Test]
	public void RootScopedServerFindsDescendantInAttachedHarness()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AttachToHarness(harness.Process.Id, nameof(RootScopedServerFindsDescendantInAttachedHarness));

		var buttonGroup = driver.GetElement(
			element => element.TypeName == "GroupBox" && element[KnownProperties.Header] == "Buttons",
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

		var button = driver.GetElement(
			buttonGroup,
			element => element.TypeName == "Button"
				&& element[KnownProperties.AutomationId] == "OpenFileDialogButton"
				&& element[KnownProperties.Content] == "Open file dialog",
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

		Assert.That(button.TargetId, Is.Not.Empty);
		Assert.That(button[KnownProperties.AutomationId].ToString(), Is.EqualTo("OpenFileDialogButton"));
	}

	[Test]
	public void RootPredicateServerFindsDescendantInAttachedHarnessInOneCommand()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AttachToHarness(harness.Process.Id, nameof(RootPredicateServerFindsDescendantInAttachedHarnessInOneCommand));

		var button = driver.GetElement(
			root => root[KnownProperties.AutomationId] == "ButtonControls",
			element => element.TypeName == "Button"
				&& element[KnownProperties.AutomationId] == "OpenFileDialogButton"
				&& element[KnownProperties.Content] == "Open file dialog",
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

		Assert.That(button.TargetId, Is.Not.Empty);
		Assert.That(button[KnownProperties.AutomationId].ToString(), Is.EqualTo("OpenFileDialogButton"));
	}

	[Test]
	public void KeyboardCanTypeShortcutAndNavigateInAttachedHarness()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		using var driver = AttachToHarness(harness.Process.Id, nameof(KeyboardCanTypeShortcutAndNavigateInAttachedHarness));
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

	[Test]
	public void SemanticRecordingWritesJsonlSnapshotAndDeltaInAttachedHarness()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		var outputPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"semantic-recording-{Guid.NewGuid():N}.jsonl");
		if (File.Exists(outputPath))
			File.Delete(outputPath);

		using var driver = AttachToHarness(harness.Process.Id, nameof(SemanticRecordingWritesJsonlSnapshotAndDeltaInAttachedHarness), enableTestRecording: false);
		const string expectedEventText = "HelloWorldButton_Click event triggered.";

		using (driver.StartSemanticRecording(outputPath, new SemanticRecordingOptions
		{
			IntervalMs = 100,
			TextIdleMs = 25,
			MaxBatchFrames = 20,
			PropNames = MatcherPropertyNames,
			TimeoutMs = 30_000,
		}))
		{
			Assert.That(
				SpinWait.SpinUntil(() => RecordingFileContainsSnapshot(outputPath, "HelloWorldButton"), TimeSpan.FromSeconds(10)),
				Is.True,
				"Semantic recording did not write the initial harness snapshot.");

			FindByAutomationId(driver, "HelloWorldButton").Click();
			WaitForElementText(driver, "HelloWorldInput", expectedEventText);

			Assert.That(
				SpinWait.SpinUntil(() => RecordingFileContainsDelta(outputPath, "HelloWorldInput", expectedEventText), TimeSpan.FromSeconds(10)),
				Is.True,
				"Semantic recording did not write a delta after the harness UI changed.");
		}

		var frames = File.ReadAllLines(outputPath)
			.Where(static line => !string.IsNullOrWhiteSpace(line))
			.Select(JObject.Parse)
			.ToArray();

		Assert.That(frames.Select(RecordingKind), Does.Contain("recording-started"));
		Assert.That(
			frames.Any(static frame => RecordingKind(frame) == "snapshot"
				&& SnapshotNodes(frame).Any(static node => CompactNodeHasAutomationId(node, "HelloWorldButton"))),
			Is.True);
		Assert.That(
			frames.Where(static frame => RecordingKind(frame) == "snapshot")
				.SelectMany(SnapshotNodes)
				.Select(static node => (string?)node["id"])
				.Where(static targetId => !string.IsNullOrWhiteSpace(targetId)),
			Is.Unique);
		var deltaFrames = frames.Where(static frame => RecordingKind(frame) == "delta").ToArray();
		Assert.That(deltaFrames.Any(DeltaHasChanges), Is.True);
		Assert.That(
			deltaFrames.Any(frame => DeltaNodes(frame)
				.Any(static node => CompactNodeHasAutomationId(node, "HelloWorldInput")
					&& CompactNodeHasText(node, expectedEventText))),
			Is.True);
	}

	[Test]
	public void AttachFailureIncludesInjectorDiagnostics()
	{
		using var harness = HarnessProcess.Start(ResolveHelloWorldExecutablePath());
		var missingPayloadRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"missing-payload-{Guid.NewGuid():N}");
		Directory.CreateDirectory(missingPayloadRoot);
		try
		{
			var exception = Assert.Throws<AppConnectionException>(() =>
				AppDriver.AttachTo(harness.Process.Id, new AppDriverAttachOptions
				{
					Timeout = TimeSpan.FromSeconds(30),
					PayloadRoot = missingPayloadRoot,
				}));

			Assert.That(exception!.Message, Does.Contain("Target injection failed: Injector launcher exited with code 6."));
			Assert.That(exception.StartupLogTail, Does.Contain("Injector log tail"));
			Assert.That(exception.StartupLogTail, Does.Contain("Could not find payload assembly"));
		}
		finally
		{
			Directory.Delete(missingPayloadRoot, recursive: true);
		}
	}

	private static Element AttachAndFind(int processId, string automationId)
	{
		using var driver = AttachToHarness(processId, $"{nameof(AttachToRunningProcessCanDisconnectAndReattach)}-{automationId}");

		Assert.That(driver.Connection.OwnsProcess, Is.False);
		return driver.GetElement(ElementSelector.ByAutomationId(automationId));
	}

	private static AppDriver AttachToHarness(int processId, string recordingLabel, bool enableTestRecording = true)
	{
		var options = new AppDriverAttachOptions
		{
			Timeout = TimeSpan.FromSeconds(30),
			PayloadRoot = ResolvePayloadRoot(),
		};
		if (enableTestRecording)
			TestSemanticRecording.Configure(options, recordingLabel);

		return AppDriver.AttachTo(processId, options);
	}

	private static Element FindByAutomationId(AppDriver driver, string automationId) =>
		driver.GetElement(
			element => element[KnownProperties.AutomationId] == automationId,
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

	private static Element WaitForElementText(AppDriver driver, string automationId, string expectedText) =>
		driver.GetElement(
			element => element[KnownProperties.AutomationId] == automationId && element[KnownProperties.Text] == expectedText,
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

	private static Element WaitForMenuHeaderOpen(AppDriver driver) =>
		driver.GetElement(
			element => element[KnownProperties.AutomationId] == "MenuHeader" && element[KnownProperties.IsSubmenuOpen] == true,
			timeoutMs: 30_000,
			propNames: MatcherPropertyNames);

	private static bool RecordingFileContainsSnapshot(string outputPath, string automationId)
	{
		if (!File.Exists(outputPath))
			return false;

		try
		{
			return ReadRecordingLinesShared(outputPath)
				.Select(JObject.Parse)
				.Any(frame => RecordingKind(frame) == "snapshot"
					&& SnapshotNodes(frame).Any(node => CompactNodeHasAutomationId(node, automationId)));
		}
		catch (JsonException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static bool RecordingFileContainsDelta(string outputPath, string automationId, string expectedText)
	{
		if (!File.Exists(outputPath))
			return false;

		try
		{
			return ReadRecordingLinesShared(outputPath)
				.Select(JObject.Parse)
				.Any(frame => RecordingKind(frame) == "delta"
					&& DeltaHasChanges(frame)
					&& DeltaNodes(frame).Any(node => CompactNodeHasAutomationId(node, automationId)
						&& CompactNodeHasText(node, expectedText)));
		}
		catch (JsonException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static IEnumerable<string> ReadRecordingLinesShared(string outputPath)
	{
		using var stream = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);
		while (reader.ReadLine() is { } line)
			if (!string.IsNullOrWhiteSpace(line))
				yield return line;
	}

	private static string? RecordingKind(JObject frame) =>
		(string?)frame["kind"];

	private static IEnumerable<JToken> SnapshotNodes(JObject frame) =>
		frame["snapshot"]?["nodes"]?.Children() ?? Enumerable.Empty<JToken>();

	private static IEnumerable<JToken> DeltaNodes(JObject frame) =>
		DeltaNodes(frame, "added").Concat(DeltaNodes(frame, "changed"));

	private static IEnumerable<JToken> DeltaNodes(JObject frame, string section) =>
		frame["delta"]?[section]?.Children() ?? Enumerable.Empty<JToken>();

	private static bool DeltaHasChanges(JObject frame) =>
		((int?)frame["delta"]?["addedCount"] ?? 0) > 0
		|| ((int?)frame["delta"]?["changedCount"] ?? 0) > 0
		|| ((int?)frame["delta"]?["removedCount"] ?? 0) > 0;

	private static bool CompactNodeHasAutomationId(JToken node, string automationId) =>
		string.Equals((string?)node["automationId"], automationId, StringComparison.Ordinal);

	private static bool CompactNodeHasText(JToken node, string text) =>
		string.Equals((string?)node["text"], text, StringComparison.Ordinal);

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
		foreach (var propertyName in new[] { KnownProperties.Header, KnownProperties.Text, KnownProperties.Content, KnownProperties.AutomationName, KnownProperties.Name })
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
