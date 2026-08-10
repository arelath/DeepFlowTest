namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Tests.Fakes;
using NUnit.Framework;
using FakeSession = DeepFlowTest.Tests.Fakes.FakeAppDriverCommandSession;

[TestFixture]
public sealed class AppDriverTests
{
	[Test]
	public void LaunchUsesBackendAndOwnsProcessByDefault()
	{
		var backend = new FakeBackend();
		var factory = CreateBackendOnlyFactory(backend);

		using var driver = factory.Launch("target.exe", new AppDriverLaunchOptions { Arguments = "--demo" });

		Assert.That(backend.LaunchedExecutablePath, Is.EqualTo(Path.GetFullPath("target.exe")));
		Assert.That(backend.LaunchedOptions!.Arguments, Is.EqualTo("--demo"));
		Assert.That(driver.Connection.OwnsProcess, Is.True);
		driver.Dispose();
		Assert.That(((FakeTargetProcess)driver.Connection.TargetProcess).KillCount, Is.EqualTo(1));
	}

	[Test]
	public void PathLaunchExpandsEnvironmentVariablesAndNormalizesExecutablePath()
	{
		var root = Path.Combine(Path.GetTempPath(), $"DeepFlowTestLaunch-{Guid.NewGuid():N}");
		var previous = Environment.GetEnvironmentVariable("DFT_LAUNCH_ROOT");
		Environment.SetEnvironmentVariable("DFT_LAUNCH_ROOT", root);
		try
		{
			var backend = new FakeBackend();
			var factory = CreateBackendOnlyFactory(backend);

			using var driver = factory.Launch(@"%DFT_LAUNCH_ROOT%\target.exe");

			Assert.That(backend.LaunchedExecutablePath, Is.EqualTo(Path.GetFullPath(Path.Combine(root, "target.exe"))));
		}
		finally
		{
			Environment.SetEnvironmentVariable("DFT_LAUNCH_ROOT", previous);
		}
	}

	[Test]
	public void ProcessStartInfoLaunchPreservesCallerStartInfo()
	{
		var backend = new FakeBackend();
		var factory = CreateBackendOnlyFactory(backend);
		var startInfo = new ProcessStartInfo("relative-target.exe", "--demo")
		{
			WorkingDirectory = @"C:\TestEnvironment",
			UseShellExecute = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			Verb = "runas",
		};

		using var driver = factory.Launch(startInfo);

		Assert.That(backend.LaunchedExecutablePath, Is.EqualTo("relative-target.exe"));
		Assert.That(backend.LaunchedOptions!.ProcessStartInfo, Is.SameAs(startInfo));
		Assert.That(backend.LaunchedOptions.Arguments, Is.EqualTo("--demo"));
		Assert.That(backend.LaunchedOptions.WorkingDirectory, Is.EqualTo(@"C:\TestEnvironment"));
		Assert.That(backend.LaunchedOptions.ProcessStartInfo!.UseShellExecute, Is.True);
		Assert.That(backend.LaunchedOptions.ProcessStartInfo.WindowStyle, Is.EqualTo(ProcessWindowStyle.Hidden));
		Assert.That(backend.LaunchedOptions.ProcessStartInfo.Verb, Is.EqualTo("runas"));
	}

	[Test]
	public void DefaultBackendLaunchProcessStartInfoPreservesCustomEnvironment()
	{
		var tempDirectory = Path.Combine(Path.GetTempPath(), $"DeepFlowTestLaunch-{Guid.NewGuid():N}");
		var outputPath = Path.Combine(tempDirectory, "env.txt");
		var environmentName = $"DFT_TEST_MODE_{Guid.NewGuid():N}";
		const string ExpectedValue = "preserved";
		Directory.CreateDirectory(tempDirectory);
		Environment.SetEnvironmentVariable(environmentName, null);
		try
		{
			var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
			{
				Arguments = $"/c echo %{environmentName}%> env.txt",
				WorkingDirectory = tempDirectory,
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			startInfo.Environment[environmentName] = ExpectedValue;
			var options = new AppDriverLaunchOptions
			{
				AllowInjection = false,
				PipeName = $"deepflowtest-launch-{Guid.NewGuid():N}",
				ProcessStartInfo = startInfo,
			};
			var backend = new DefaultAppDriverBackend();

			using var connection = backend.Launch(startInfo.FileName, options);
			var text = WaitForFileText(outputPath);

			Assert.That(text, Is.EqualTo(ExpectedValue));
			Assert.That(connection.InjectorState, Is.EqualTo(AppConnectionInjectorState.InjectionSkipped));
		}
		finally
		{
			Environment.SetEnvironmentVariable(environmentName, null);
			Directory.Delete(tempDirectory, recursive: true);
		}
	}

	[Test]
	public void DefaultBackendPathLaunchCreatesExpandedNormalizedStartInfo()
	{
		var root = Path.Combine(Path.GetTempPath(), $"DeepFlowTestLaunch-{Guid.NewGuid():N}");
		var previous = Environment.GetEnvironmentVariable("DFT_LAUNCH_ROOT");
		Environment.SetEnvironmentVariable("DFT_LAUNCH_ROOT", root);
		try
		{
			var options = new AppDriverLaunchOptions
			{
				Arguments = "--demo",
				WorkingDirectory = root,
			};

			var startInfo = AppDriverLaunch.ResolveStartInfo(@"%DFT_LAUNCH_ROOT%\target.exe", options);

			Assert.That(startInfo.FileName, Is.EqualTo(Path.GetFullPath(Path.Combine(root, "target.exe"))));
			Assert.That(startInfo.Arguments, Is.EqualTo("--demo"));
			Assert.That(startInfo.WorkingDirectory, Is.EqualTo(root));
		}
		finally
		{
			Environment.SetEnvironmentVariable("DFT_LAUNCH_ROOT", previous);
		}
	}

	[Test]
	public void AttachByPidUsesBackendAndDoesNotOwnProcess()
	{
		var backend = new FakeBackend();
		var factory = CreateBackendOnlyFactory(backend);

		using var driver = factory.AttachTo(42);

		Assert.That(backend.AttachedProcessId, Is.EqualTo(42));
		Assert.That(driver.Connection.OwnsProcess, Is.False);
		driver.Dispose();
		Assert.That(((FakeTargetProcess)driver.Connection.TargetProcess).KillCount, Is.EqualTo(0));
	}

	[Test]
	public void ProcessNameResolutionUsesExactThenContainsAndRejectsAmbiguity()
	{
		var processes = new ITargetProcess[]
		{
			new FakeTargetProcess { Id = 1, ProcessName = "SampleApp" },
			new FakeTargetProcess { Id = 2, ProcessName = "OtherSampleApp" },
			new FakeTargetProcess { Id = 3, ProcessName = "Exact" },
		};

		Assert.That(AppDriverProcessResolver.ResolveByName(processes, "Exact").Id, Is.EqualTo(3));
		Assert.That(AppDriverProcessResolver.ResolveByName(processes, "Other").Id, Is.EqualTo(2));
		Assert.That(
			() => AppDriverProcessResolver.ResolveByName(processes, "Sample"),
			Throws.TypeOf<AppDriverException>().With.Property(nameof(AppDriverException.ErrorCode)).EqualTo(AppDriverErrorCodes.AmbiguousTarget));
		Assert.That(
			() => AppDriverProcessResolver.ResolveByName(processes, "Missing"),
			Throws.TypeOf<AppDriverException>().With.Property(nameof(AppDriverException.ErrorCode)).EqualTo(AppDriverErrorCodes.TargetNotFound));
	}

	[Test]
	public void DefaultBackendAttachByNameCreatesAttachConnectionWithoutInjectionWhenPolicyDisallows()
	{
		var process = new FakeTargetProcess { Id = 99, ProcessName = "Harness" };
		var backend = new DefaultAppDriverBackend(new FakeProcessCatalog(new[] { process }));

		using var connection = backend.AttachTo("Harness", new AppDriverAttachOptions
		{
			AllowInjection = false,
			PipeName = "deepflowtest-test-pipe",
		});

		Assert.That(connection.TargetProcess.Id, Is.EqualTo(99));
		Assert.That(connection.OwnsProcess, Is.False);
		Assert.That(connection.InjectorState, Is.EqualTo(AppConnectionInjectorState.InjectionSkipped));
	}

	[Test]
	public void TimeoutOptionsExposeDeterministicElementPollingBackoff()
	{
		var options = new AppDriverAttachOptions { Timeout = TimeSpan.FromMilliseconds(1234) };

		Assert.That(options.ElementPollBackoff, Is.EqualTo(TimeoutDefaults.CreateElementPollBackoffMs().Select(static milliseconds => TimeSpan.FromMilliseconds(milliseconds))));
		Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1234)));
	}

	[Test]
	public void OptionAndSelectorCollectionsAreDefensiveCopies()
	{
		var pollBackoff = new[] { TimeSpan.FromMilliseconds(12) };
		var ignoredFailures = new[] { BindingFailureFilter.Contains("first") };
		var recordingProperties = new[] { KnownProperties.Name };
		var requestedProperties = new[] { KnownProperties.AutomationId };
		var options = new AppDriverOptions
		{
			ElementPollBackoff = pollBackoff,
			BindingFailures = new BindingFailureOptions { Ignore = ignoredFailures },
			AutoSemanticRecordingOptions = new SemanticRecordingOptions { PropNames = recordingProperties },
		};
		var selector = ElementSelector.ByName("button").WithRequestedProperties(requestedProperties);

		pollBackoff[0] = TimeSpan.FromSeconds(9);
		ignoredFailures[0] = BindingFailureFilter.Contains("changed");
		recordingProperties[0] = KnownProperties.Text;
		requestedProperties[0] = KnownProperties.Content;

		Assert.That(options.ElementPollBackoff, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(12) }));
		Assert.That(options.BindingFailures.Ignore.Single().Pattern, Is.EqualTo("first"));
		Assert.That(options.AutoSemanticRecordingOptions.PropNames, Is.EqualTo(new[] { KnownProperties.Name }));
		Assert.That(selector.RequestedPropertyNames, Is.EqualTo(new[] { KnownProperties.AutomationId }));
		Assert.Throws<NotSupportedException>(() => ((IList<TimeSpan>)options.ElementPollBackoff)[0] = TimeSpan.Zero);
	}

	[Test]
	public void SessionOptionsAreValidatedBeforeLaunchingTheTarget()
	{
		var backend = new FakeBackend();
		var factory = CreateBackendOnlyFactory(backend);

		Assert.Throws<ArgumentOutOfRangeException>(() => factory.Launch(
			"target.exe",
			new AppDriverLaunchOptions { Timeout = TimeSpan.Zero }));

		Assert.That(backend.LaunchedExecutablePath, Is.Null);
	}

	[Test]
	public void RawCommandsAreExposedOnlyThroughTheUnsafeInterface()
	{
		Assert.That(typeof(AppDriver).GetMethods().Any(static method => method.Name == "Send"), Is.False);
		Assert.That(typeof(AppDriver).GetProperty(nameof(AppDriver.UnsafeCommands))!.PropertyType, Is.EqualTo(typeof(IUnsafeAppDriverCommandSession)));
		Assert.That(typeof(NamedPipeAppDriverCommandSession).IsNotPublic, Is.True);
	}

	[Test]
	public void GetElementPollsWithConfiguredBackoff()
	{
		var session = new FakeSession(
			new FindElementCommandResponse { Status = ProtocolConstants.Statuses.NoMatch },
			new FindElementCommandResponse { Status = ProtocolConstants.Statuses.NoMatch },
			new FindElementCommandResponse { Status = ProtocolConstants.Statuses.NoMatch },
			new FindElementCommandResponse
			{
				Matches = { new FindElementMatchResponse { TargetId = "target", TypeName = "Button" } },
				MatchCount = 1,
			});
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session,
			new AppDriverOptions { ElementPollBackoff = new[] { TimeSpan.FromMilliseconds(1) }, Timeout = TimeSpan.FromMilliseconds(2500) });

		var element = driver.GetElement(ElementSelector.ByName("late"));

		Assert.That(element.TargetId, Is.EqualTo("target"));
		Assert.That(session.SentCommands.Count, Is.EqualTo(4));
	}

	[Test]
	public void ExpressionGetElementsPollsUntilTimeoutNotJustBackoffSequence()
	{
		var empty = new FindElementCommandResponse { Status = ProtocolConstants.Statuses.NoMatch };
		var match = new FindElementCommandResponse
		{
			Status = ProtocolConstants.Statuses.Ok,
			Matches =
			{
				new FindElementMatchResponse
				{
					TargetId = "late-target",
					TypeName = "Button",
					Properties = { ["Name"] = "late" },
				},
			},
			MatchCount = 1,
		};
		var session = new FakeSession(empty, empty, match);
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session,
			new AppDriverOptions { ElementPollBackoff = new[] { TimeSpan.FromMilliseconds(1) }, Timeout = TimeSpan.FromSeconds(1) });

		var elements = driver.GetElements(x => x["Name"] == "late", timeout: TimeSpan.FromMilliseconds(1500));

		Assert.That(elements.Single().TargetId, Is.EqualTo("late-target"));
		Assert.That(session.SentCommands.Count, Is.EqualTo(3));
	}

	[Test]
	public void AmbiguousExpressionElementErrorIncludesMatchedElementSummaries()
	{
		var response = new FindElementCommandResponse
		{
			Status = ProtocolConstants.Statuses.Ok,
			Matches =
			{
				new FindElementMatchResponse
				{
					TargetId = "build",
					TypeName = "MenuItem",
					Path =
					{
						new ElementPathSegmentResponse
						{
							TargetId = "window",
							TypeName = "Window",
							Properties =
							{
								["AutomationProperties.AutomationId"] = "MainWindow",
								["Name"] = "Sage",
							},
						},
						new ElementPathSegmentResponse
						{
							TargetId = "menu",
							TypeName = "Menu",
							Properties =
							{
								["AutomationProperties.AutomationId"] = "MainMenu",
							},
						},
						new ElementPathSegmentResponse
						{
							TargetId = "build",
							TypeName = "MenuItem",
							Properties =
							{
								["AutomationProperties.AutomationId"] = "BuildMenuItem",
								["Header"] = "_Build",
							},
						},
					},
					Properties =
					{
						["AutomationProperties.AutomationId"] = "BuildMenuItem",
						["Header"] = "_Build",
						["IsVisible"] = true,
					},
				},
				new FindElementMatchResponse
				{
					TargetId = "buildToolbar",
					TypeName = "MenuItem",
					Path =
					{
						new ElementPathSegmentResponse
						{
							TargetId = "window",
							TypeName = "Window",
							Properties =
							{
								["AutomationProperties.AutomationId"] = "MainWindow",
								["Name"] = "Sage",
							},
						},
						new ElementPathSegmentResponse
						{
							TargetId = "toolbar",
							TypeName = "ToolBar",
							Properties =
							{
								["AutomationProperties.AutomationId"] = "BuildToolbar",
							},
						},
						new ElementPathSegmentResponse
						{
							TargetId = "buildToolbar",
							TypeName = "MenuItem",
							Properties =
							{
								["AutomationProperties.AutomationId"] = "BuildToolbarMenuItem",
								["Header"] = "Build",
							},
						},
					},
					Properties =
					{
						["AutomationProperties.AutomationId"] = "BuildToolbarMenuItem",
						["Header"] = "Build",
						["IsVisible"] = true,
					},
				},
			},
			MatchCount = 2,
			MaxMatches = 2,
		};
		var session = new FakeSession(response);
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session,
			new AppDriverOptions { Timeout = TimeSpan.FromMilliseconds(1) });
		var normalizedHeader = "Build";

		var exception = Assert.Throws<AppDriverException>(() =>
			driver.GetElement(
				element => element.TypeName == "MenuItem" && element["Header"].ToString().Replace("_", string.Empty).Trim() == normalizedHeader,
				timeout: TimeSpan.FromMilliseconds(1),
				propNames: MatcherPropertyNames));

		Assert.That(exception!.ErrorCode, Is.EqualTo(AppDriverErrorCodes.AmbiguousTarget));
		Assert.That(exception.Message, Does.Contain("More than one element matched selector."));
		Assert.That(exception.Message, Does.Contain("\"Build\""));
		Assert.That(exception.Message, Does.Not.Contain("DisplayClass"));
		Assert.That(exception.Message, Does.Not.Contain("value("));
		Assert.That(exception.Message, Does.Contain("Matched elements:"));
		Assert.That(exception.Message, Does.Contain("TargetId=\"build\""));
		Assert.That(exception.Message, Does.Contain("AutomationProperties.AutomationId=\"BuildMenuItem\""));
		Assert.That(exception.Message, Does.Contain("Header=\"_Build\""));
		Assert.That(exception.Message, Does.Contain("Path=Window[AutomationId=\"MainWindow\", Name=\"Sage\"] > Menu[AutomationId=\"MainMenu\"] > MenuItem[AutomationId=\"BuildMenuItem\", Header=\"_Build\"]"));
		Assert.That(exception.Message, Does.Contain("TargetId=\"buildToolbar\""));
		Assert.That(exception.Message, Does.Contain("Path=Window[AutomationId=\"MainWindow\", Name=\"Sage\"] > ToolBar[AutomationId=\"BuildToolbar\"] > MenuItem[AutomationId=\"BuildToolbarMenuItem\", Header=\"Build\"]"));
		Assert.That(exception.Message, Does.Contain("Make the selector more specific"));
	}

	[Test]
	public void CapturedElementPredicateFallsBackToClientSnapshotMatching()
	{
		var snapshot = VisualTreeSnapshot.Create(
			1,
			new[]
			{
				new VisualTreeNodeDto
				{
					TargetId = "build",
					TypeName = "MenuItem",
					Properties =
					{
						["Header"] = "Build",
						["IsEnabled"] = true,
					},
				},
				new VisualTreeNodeDto
				{
					TargetId = "check",
					TypeName = "MenuItem",
					Properties =
					{
						["Header"] = "Check",
						["IsEnabled"] = false,
					},
				},
			});
		var session = new FakeSession(snapshot);
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session,
			new AppDriverOptions { Timeout = TimeSpan.FromMilliseconds(1) });
		Func<Element, bool> predicate = element =>
			element.TypeName == "MenuItem"
			&& element["Header"] == "Build"
			&& element["IsEnabled"];

		var element = driver.GetElement(candidate => predicate(candidate), timeout: TimeSpan.FromMilliseconds(1));

		Assert.That(element.TargetId, Is.EqualTo("build"));
		Assert.That(session.SentCommands.OfType<FindElementCommandRequest>(), Is.Empty);
		var command = session.SentCommands.OfType<GetVisualTreeCommandRequest>().Single();
		Assert.That(command.PropNames, Does.Contain("Header"));
		Assert.That(command.PropNames, Does.Contain("IsEnabled"));
		Assert.That(command.MaxNodeCount, Is.EqualTo(50_000));
	}

	[Test]
	public void CapturedElementPredicatePollsClientSnapshotsUntilMatch()
	{
		var emptySnapshot = VisualTreeSnapshot.Create(1, Array.Empty<VisualTreeNodeDto>());
		var matchSnapshot = VisualTreeSnapshot.Create(
			2,
			new[]
			{
				new VisualTreeNodeDto
				{
					TargetId = "late",
					TypeName = "Button",
					Properties =
					{
						["Content"] = "Late",
						["IsEnabled"] = true,
					},
				},
			});
		var session = new FakeSession(emptySnapshot, matchSnapshot);
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session,
			new AppDriverOptions { ElementPollBackoff = new[] { TimeSpan.FromMilliseconds(1) }, Timeout = TimeSpan.FromMilliseconds(250) });
		var expectedContent = "Late";
		Func<Element, bool> predicate = element =>
			element.TypeName == "Button"
			&& element["Content"] == expectedContent
			&& element["IsEnabled"];

		var element = driver.GetElement(candidate => predicate(candidate), timeout: TimeSpan.FromMilliseconds(250));

		Assert.That(element.TargetId, Is.EqualTo("late"));
		Assert.That(session.SentCommands.OfType<GetVisualTreeCommandRequest>().Count(), Is.EqualTo(2));
		Assert.That(session.SentCommands.OfType<FindElementCommandRequest>(), Is.Empty);
	}

	[Test]
	public void CapturedElementPredicateGetElementsReturnsAllClientSnapshotMatches()
	{
		var snapshot = VisualTreeSnapshot.Create(
			1,
			new[]
			{
				new VisualTreeNodeDto
				{
					TargetId = "build",
					TypeName = "MenuItem",
					Properties =
					{
						["Header"] = "Build",
						["IsEnabled"] = true,
					},
				},
				new VisualTreeNodeDto
				{
					TargetId = "rebuild",
					TypeName = "MenuItem",
					Properties =
					{
						["Header"] = "Rebuild",
						["IsEnabled"] = true,
					},
				},
				new VisualTreeNodeDto
				{
					TargetId = "disabled",
					TypeName = "MenuItem",
					Properties =
					{
						["Header"] = "Build disabled",
						["IsEnabled"] = false,
					},
				},
			});
		var session = new FakeSession(snapshot);
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session,
			new AppDriverOptions { Timeout = TimeSpan.FromMilliseconds(1) });
		Func<Element, bool> predicate = element =>
			element.TypeName == "MenuItem"
			&& element["Header"].ToString().IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0
			&& element["IsEnabled"];

		var elements = driver.GetElements(candidate => predicate(candidate), timeout: TimeSpan.FromMilliseconds(1));

		Assert.That(elements.Select(static element => element.TargetId), Is.EqualTo(new[] { "build", "rebuild" }));
		Assert.That(session.SentCommands.OfType<GetVisualTreeCommandRequest>().Single().PropNames, Does.Contain("Header"));
		Assert.That(session.SentCommands.OfType<FindElementCommandRequest>(), Is.Empty);
	}

	[Test]
	public void CapturedTypedElementPredicateReturnsTypedClientSnapshotMatches()
	{
		var snapshot = VisualTreeSnapshot.Create(
			1,
			new[]
			{
				new VisualTreeNodeDto
				{
					TargetId = "run",
					TypeName = "Button",
					Properties =
					{
						["Content"] = "Run",
						["IsEnabled"] = true,
					},
				},
				new VisualTreeNodeDto
				{
					TargetId = "stop",
					TypeName = "Button",
					Properties =
					{
						["Content"] = "Stop",
						["IsEnabled"] = true,
					},
				},
			});
		var session = new FakeSession(snapshot);
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session);
		Func<TestButton, bool> predicate = button =>
			button.TypeName == "Button"
			&& button["Content"] == "Run"
			&& button["IsEnabled"];

		var buttons = driver.GetElements<TestButton>(button => predicate(button), maxMatches: 10);

		Assert.That(buttons.Single().TargetId, Is.EqualTo("run"));
		Assert.That(buttons.Single(), Is.TypeOf<TestButton>());
		Assert.That(session.SentCommands.OfType<GetVisualTreeCommandRequest>().Single().PropNames, Does.Contain("Content"));
		Assert.That(session.SentCommands.OfType<FindElementCommandRequest>(), Is.Empty);
	}

	[Test]
	public void ElementHelperMethodPredicateFallsBackToClientSnapshotMatching()
	{
		var snapshot = VisualTreeSnapshot.Create(
			1,
			new[]
			{
				new VisualTreeNodeDto
				{
					TargetId = "menu",
					TypeName = "MenuItem",
					ChildIds = { "menu-text" },
					Properties =
					{
						["Header"] = "File",
						["IsEnabled"] = true,
					},
				},
				new VisualTreeNodeDto
				{
					TargetId = "menu-text",
					ParentId = "menu",
					TypeName = "TextBlock",
					Properties =
					{
						["Text"] = "Open Document",
						["IsEnabled"] = true,
					},
				},
			});
		var session = new FakeSession(snapshot);
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session);
		var normalizedHeader = NormalizeMenuHeader("Open Document");

		var element = driver.GetElement(
			candidate => string.Equals(candidate.TypeName, "MenuItem", StringComparison.Ordinal)
				&& ElementOrDescendantTextMatches(candidate, normalizedHeader, 4),
			timeout: TimeSpan.FromMilliseconds(1),
			propNames: MatcherPropertyNames);

		Assert.That(element.TargetId, Is.EqualTo("menu"));
		Assert.That(session.SentCommands.OfType<FindElementCommandRequest>(), Is.Empty);
		var command = session.SentCommands.OfType<GetVisualTreeCommandRequest>().Single();
		Assert.That(command.PropNames, Does.Contain("Header"));
		Assert.That(command.PropNames, Does.Contain("Text"));
		Assert.That(command.MaxNodeCount, Is.EqualTo(50_000));
	}

	[Test]
	public void RootScopedElementExpressionFindsDescendantsOnServer()
	{
		var session = new FakeSession(
			new FindElementCommandResponse
			{
				Status = ProtocolConstants.Statuses.Ok,
				Matches =
				{
					new FindElementMatchResponse
					{
						TargetId = "root",
						TypeName = "StackPanel",
						Properties = { ["Name"] = "rootPanel" },
					},
				},
				MatchCount = 1,
			},
			new FindElementCommandResponse
			{
				Status = ProtocolConstants.Statuses.Ok,
				Matches =
				{
					new FindElementMatchResponse
					{
						TargetId = "child",
						TypeName = "Button",
						Properties =
						{
							["Content"] = "Open",
							["IsEnabled"] = true,
						},
					},
				},
				MatchCount = 1,
			});
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session);
		var root = driver.GetElement(ElementSelector.ByName("rootPanel"));

		var child = driver.GetElement(
			root,
			element => element.TypeName == "Button" && element["Content"] == "Open" && element["IsEnabled"],
			timeout: TimeSpan.FromMilliseconds(1),
			propNames: ["Content", "IsEnabled"]);

		Assert.That(child.TargetId, Is.EqualTo("child"));
		var scopedCommand = session.SentCommands.OfType<FindElementCommandRequest>().Last();
		Assert.That(scopedCommand.RootTargetId, Is.EqualTo("root"));
		Assert.That(scopedCommand.IncludeRoot, Is.False);
		Assert.That(scopedCommand.MatcherCode, Is.TypeOf<Eval>());
		Assert.That(scopedCommand.PropNames, Does.Contain("Content"));
		Assert.That(scopedCommand.PropNames, Does.Contain("IsEnabled"));
		Assert.That(scopedCommand.MaxNodeCount, Is.EqualTo(50_000));
	}

	[Test]
	public void RootScopedNoMatchErrorIncludesElementsUnderRoot()
	{
		var session = new RootNoMatchDiagnosticSession();
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session,
			new AppDriverOptions { Timeout = TimeSpan.FromMilliseconds(1) });
		var root = driver.GetElement(ElementSelector.ByName("rootPanel"));

		var exception = Assert.Throws<AppDriverException>(() =>
			driver.GetElement(
				root,
				element => element.TypeName == "MenuItem" && element["Header"] == "Build",
				timeout: TimeSpan.FromMilliseconds(1),
				propNames: MatcherPropertyNames));

		Assert.That(exception!.ErrorCode, Is.EqualTo(AppDriverErrorCodes.TargetNotFound));
		Assert.That(exception.Message, Does.Contain("No element matched selector."));
		Assert.That(exception.Message, Does.Contain("under 'root'"));
		Assert.That(exception.Message, Does.Contain("Elements currently under 'root'"));
		Assert.That(exception.Message, Does.Contain("TargetId=\"file\""));
		Assert.That(exception.Message, Does.Contain("TypeName=\"MenuItem\""));
		Assert.That(exception.Message, Does.Contain("Header=\"File\""));
		Assert.That(exception.Message, Does.Contain("Path=StackPanel[Name=\"rootPanel\"] > MenuItem[Header=\"File\"]"));
		Assert.That(session.SentCommands.OfType<GetVisualTreeCommandRequest>().Single().RootTargetId, Is.EqualTo("root"));
		Assert.That(
			session.SentCommands.OfType<GetVisualTreeCommandRequest>().Single().MaxNodeCount,
			Is.EqualTo(VisualTreeDefaults.DefaultMaxNodeCount));
	}

	[Test]
	public void RootPredicateElementExpressionFindsDescendantsInOneServerCommand()
	{
		var session = new FakeSession(new FindElementCommandResponse
		{
			Status = ProtocolConstants.Statuses.Ok,
			Matches =
			{
				new FindElementMatchResponse
				{
					TargetId = "child",
					TypeName = "Button",
					Properties =
					{
						["Content"] = "Open",
						["IsEnabled"] = true,
					},
				},
			},
			MatchCount = 1,
		});
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session);

		var child = driver.GetElement(
			root => root.TypeName == "GroupBox" && root["Header"] == "Actions",
			element => element.TypeName == "Button" && element["Content"] == "Open" && element["IsEnabled"],
			timeout: TimeSpan.FromMilliseconds(1),
			propNames: ["Header", "Content", "IsEnabled"]);

		Assert.That(child.TargetId, Is.EqualTo("child"));
		var command = session.SentCommands.OfType<FindElementCommandRequest>().Single();
		Assert.That(command.RootTargetId, Is.Null);
		Assert.That(command.IncludeRoot, Is.False);
		Assert.That(command.RootMatcherCode, Is.TypeOf<Eval>());
		Assert.That(command.RootMatcherHash, Is.Not.Empty);
		Assert.That(command.MatcherCode, Is.TypeOf<Eval>());
		Assert.That(command.MatcherHash, Is.Not.Empty);
		Assert.That(command.PropNames, Does.Contain("Header"));
		Assert.That(command.PropNames, Does.Contain("Content"));
		Assert.That(command.PropNames, Does.Contain("IsEnabled"));
		Assert.That(command.MaxNodeCount, Is.EqualTo(50_000));
	}

	[Test]
	public void DefaultInjectorLauncherPathUsesArchitectureResourceLayoutWhenPresent()
	{
		var architecture = Environment.Is64BitProcess ? "x64" : "x86";
		var resourceDirectory = Path.Combine(AppContext.BaseDirectory, "DeepFlowTestResources", architecture);
		var launcherPath = Path.Combine(resourceDirectory, $"DeepFlowTest.InjectorLauncher.{architecture}.exe");
		var created = false;
		if (!File.Exists(launcherPath))
		{
			Directory.CreateDirectory(resourceDirectory);
			File.WriteAllText(launcherPath, string.Empty);
			created = true;
		}
		try
		{
			var options = new AppDriverOptions();

			Assert.That(options.InjectorLauncherPath, Is.EqualTo(launcherPath));
		}
		finally
		{
			if (created)
				File.Delete(launcherPath);
		}
	}

	private static AppDriverFactory CreateBackendOnlyFactory(FakeBackend backend) =>
		new(backend, static (_, _) => new FakeSession());

	private sealed class FakeBackend : IAppDriverBackend
	{
		public string? LaunchedExecutablePath { get; private set; }

		public AppDriverLaunchOptions? LaunchedOptions { get; private set; }

		public int? AttachedProcessId { get; private set; }

		public AppConnection Launch(string executablePath, AppDriverLaunchOptions options)
		{
			LaunchedExecutablePath = executablePath;
			LaunchedOptions = options;
			return AppConnection.ForLaunch(new FakeTargetProcess(), options.PipeName ?? "launch-pipe");
		}

		public AppConnection AttachTo(int processId, AppDriverAttachOptions options)
		{
			AttachedProcessId = processId;
			return AppConnection.ForAttach(new FakeTargetProcess { Id = processId }, options.PipeName ?? "attach-pipe");
		}

		public AppConnection AttachTo(string processName, AppDriverAttachOptions options)
		{
			return AppConnection.ForAttach(new FakeTargetProcess { ProcessName = processName }, options.PipeName ?? "attach-name-pipe");
		}
	}

	private sealed class RootNoMatchDiagnosticSession : IUnsafeAppDriverCommandSession
	{
		public List<IpcCommand> SentCommands { get; } = [];

		public TResponse Send<TResponse>(IpcCommand command)
		{
			SentCommands.Add(command);
			return command switch
			{
				FindElementCommandRequest { RootTargetId: null, Selector: { Name: "rootPanel" } } => (TResponse)(object)new FindElementCommandResponse
				{
					Status = ProtocolConstants.Statuses.Ok,
					Matches =
					{
						new FindElementMatchResponse
						{
							TargetId = "root",
							TypeName = "StackPanel",
							Properties = { ["Name"] = "rootPanel" },
						},
					},
					MatchCount = 1,
				},
				FindElementCommandRequest { RootTargetId: "root" } => (TResponse)(object)new FindElementCommandResponse
				{
					Status = ProtocolConstants.Statuses.NoMatch,
				},
				GetVisualTreeCommandRequest { RootTargetId: "root" } => (TResponse)(object)VisualTreeSnapshot.Create(
					2,
					new[]
					{
						new VisualTreeNodeDto
						{
							TargetId = "root",
							TypeName = "StackPanel",
							IsRoot = true,
							ChildIds = { "file" },
							Properties = { ["Name"] = "rootPanel" },
						},
						new VisualTreeNodeDto
						{
							TargetId = "file",
							ParentId = "root",
							TypeName = "MenuItem",
							Properties =
							{
								["Header"] = "File",
								["IsVisible"] = true,
							},
						},
					}),
				_ => throw new InvalidOperationException($"Unexpected command: {command.GetType().Name}"),
			};
		}
	}

	private sealed class FakeProcessCatalog : IProcessCatalog
	{
		private readonly IReadOnlyList<ITargetProcess> processes;

		public FakeProcessCatalog(IReadOnlyList<ITargetProcess> processes)
		{
			this.processes = processes;
		}

		public ITargetProcess GetById(int processId)
		{
			foreach (var process in processes)
				if (process.Id == processId)
					return process;

			throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, "missing");
		}

		public IReadOnlyList<ITargetProcess> GetProcesses() => processes;
	}

	private sealed class TestButton : Element<TestButton>
	{
		public TestButton(Element source)
			: base(source)
		{
		}
	}

	private static readonly IReadOnlyList<string> MatcherPropertyNames =
	[
		KnownProperties.Name,
		KnownProperties.AutomationName,
		KnownProperties.AutomationId,
		KnownProperties.Text,
		KnownProperties.Content,
		KnownProperties.Header,
		KnownProperties.IsEnabled,
		KnownProperties.IsVisible,
	];

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

	private static string WaitForFileText(string path)
	{
		var found = SpinWait.SpinUntil(() => File.Exists(path), TimeSpan.FromSeconds(5));
		Assert.That(found, Is.True, $"Expected file '{path}' to be written.");
		return File.ReadAllText(path).Trim();
	}

}
