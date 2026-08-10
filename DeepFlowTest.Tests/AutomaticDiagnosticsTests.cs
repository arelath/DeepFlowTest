namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

[TestFixture]
public sealed class AutomaticDiagnosticsTests
{
	[Test]
	public void FailureOnlyFlushesTraceFinalStateDiagnosticsAndManifestAfterMarkedFailure()
	{
		var root = CreateCleanDirectory("failure-only");
		var sink = new FakeArtifactSink(root, "failed test");
		var session = new DiagnosticsCommandSession();
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe", "dotnet"),
			session,
			new AppDriverOptions
			{
				AutomaticDiagnostics = new AutomaticDiagnosticsOptions
				{
					OutputDirectory = root,
					ArtifactSink = sink,
					MaximumArtifactSizeBytes = 1024 * 1024,
					FailureBufferSizeBytes = 128 * 1024,
				},
			});

		Assert.That(driver.AutomaticSemanticRecordingOutputPath, Is.Null);
		Assert.That(SpinWait.SpinUntil(() => session.FrameRead, TimeSpan.FromSeconds(2)), Is.True);
		driver.MarkDiagnosticsFailure(new InvalidOperationException("application failed"));
		Assert.DoesNotThrow(driver.Dispose);

		var manifestPath = driver.AutomaticDiagnosticsManifestPath;
		Assert.That(manifestPath, Is.Not.Null);
		Assert.That(File.Exists(manifestPath!), Is.True);
		var manifest = JObject.Parse(File.ReadAllText(manifestPath!));
		Assert.That((bool?)manifest["failed"], Is.True);
		Assert.That((string?)manifest["mode"], Is.EqualTo(nameof(AutomaticDiagnosticsMode.FailureOnly)));
		Assert.That((string?)manifest["payloadFrameworkFamily"], Is.EqualTo("dotnet"));
		Assert.That((string?)manifest["clientVersion"], Is.Not.Empty);
		Assert.That((string?)manifest["payloadVersion"], Is.Not.Empty);
		Assert.That(ArtifactKinds(manifest), Does.Contain("semantic-trace"));
		Assert.That(ArtifactKinds(manifest), Does.Contain("final-screenshot"));
		Assert.That(ArtifactKinds(manifest), Does.Contain("final-tree"));
		Assert.That(ArtifactKinds(manifest), Does.Contain("protocol-log"));
		Assert.That(sink.Attachments.Select(Path.GetFileName), Does.Contain("manifest.json"));
		Assert.That(driver.Diagnostics, Is.Empty);
	}

	[TestCase(AutomaticDiagnosticsMode.Manual)]
	[TestCase(AutomaticDiagnosticsMode.Off)]
	public void ManualAndOffDoNotStartAutomaticRecording(AutomaticDiagnosticsMode mode)
	{
		var session = new DiagnosticsCommandSession();
		using var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe"),
			session,
			new AppDriverOptions
			{
				AutomaticDiagnostics = new AutomaticDiagnosticsOptions { Mode = mode },
			});

		Assert.That(session.StartRequest, Is.Null);
	}

	[Test]
	public void AlwaysWritesContinuouslyAndKeepsSuccessfulTraceWhenRequested()
	{
		var root = CreateCleanDirectory("always");
		var session = new DiagnosticsCommandSession();
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe"),
			session,
			new AppDriverOptions
			{
				AutomaticDiagnostics = new AutomaticDiagnosticsOptions
				{
					Mode = AutomaticDiagnosticsMode.Always,
					OutputDirectory = root,
					RetentionPolicy = DiagnosticsRetentionPolicy.KeepAll,
					CaptureFinalScreenshotOnFailure = false,
					CaptureFinalTreeOnFailure = false,
				},
			});

		var tracePath = driver.AutomaticSemanticRecordingOutputPath;
		Assert.That(tracePath, Is.Not.Null);
		Assert.That(SpinWait.SpinUntil(() => File.Exists(tracePath!) && new FileInfo(tracePath!).Length > 0, TimeSpan.FromSeconds(2)), Is.True);
		Assert.DoesNotThrow(driver.Dispose);
		Assert.That(File.Exists(tracePath!), Is.True);
		Assert.That(File.Exists(driver.AutomaticDiagnosticsManifestPath!), Is.True);
	}

	[Test]
	public void AutomaticRecorderCleanupFailureIsReportedButDisposeDoesNotThrow()
	{
		var root = CreateCleanDirectory("automatic-cleanup-failure");
		var session = new DiagnosticsCommandSession(throwWhenStopping: true);
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe"),
			session,
			new AppDriverOptions
			{
				AutomaticDiagnostics = new AutomaticDiagnosticsOptions
				{
					Mode = AutomaticDiagnosticsMode.Always,
					OutputDirectory = root,
				},
			});

		Assert.DoesNotThrow(driver.Dispose);
		Assert.That(driver.Diagnostics.Select(static diagnostic => diagnostic.Code), Does.Contain("recording-stop-failed"));
		Assert.That(File.Exists(driver.AutomaticDiagnosticsManifestPath!), Is.True);
	}

	[Test]
	public void AutomaticStartupErrorsAreLoggedAndAttachedEvenWhenTheTestItselfSucceeds()
	{
		var root = CreateCleanDirectory("startup-error-attachment");
		var sink = new FakeArtifactSink(root, "startup error test");
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe"),
			new FakeAppDriverCommandSession(),
			new AppDriverOptions
			{
				AutomaticDiagnostics = new AutomaticDiagnosticsOptions
				{
					OutputDirectory = root,
					ArtifactSink = sink,
				},
			});

		Assert.DoesNotThrow(driver.Dispose);
		Assert.That(driver.Diagnostics.Select(static diagnostic => diagnostic.Code), Does.Contain("automatic-recording-start-failed"));
		Assert.That(sink.Logged.Select(static diagnostic => diagnostic.Code), Does.Contain("automatic-recording-start-failed"));
		Assert.That(File.Exists(driver.AutomaticDiagnosticsManifestPath!), Is.True);
		Assert.That(sink.Attachments.Select(Path.GetFileName), Does.Contain("diagnostics.json"));
		Assert.That(sink.Attachments.Select(Path.GetFileName), Does.Contain("manifest.json"));
	}

	[Test]
	public void TestResultFailureAndDriverCommandFailureBothTriggerFailureOnlyFlush()
	{
		var sinkRoot = CreateCleanDirectory("sink-detected-failure");
		var sink = new FakeArtifactSink(sinkRoot, "sink failed test", hasFailed: true);
		var sinkDriver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe"),
			new DiagnosticsCommandSession(),
			new AppDriverOptions
			{
				AutomaticDiagnostics = new AutomaticDiagnosticsOptions
				{
					OutputDirectory = sinkRoot,
					ArtifactSink = sink,
					CaptureFinalScreenshotOnFailure = false,
					CaptureFinalTreeOnFailure = false,
				},
			});
		Assert.DoesNotThrow(sinkDriver.Dispose);
		Assert.That(File.Exists(sinkDriver.AutomaticDiagnosticsManifestPath!), Is.True);

		var commandRoot = CreateCleanDirectory("command-detected-failure");
		var commandDriver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe"),
			new DiagnosticsCommandSession(throwOnTree: true),
			new AppDriverOptions
			{
				AutomaticDiagnostics = new AutomaticDiagnosticsOptions
				{
					OutputDirectory = commandRoot,
					CaptureFinalScreenshotOnFailure = false,
					CaptureFinalTreeOnFailure = false,
				},
			});
		Assert.Throws<InvalidOperationException>(() => commandDriver.GetVisualTree());
		Assert.DoesNotThrow(commandDriver.Dispose);
		Assert.That(File.Exists(commandDriver.AutomaticDiagnosticsManifestPath!), Is.True);
	}

	[Test]
	public void FailureArtifactsStayWithinTheConfiguredPerTestBudget()
	{
		var root = CreateCleanDirectory("artifact-budget");
		var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe"),
			new DiagnosticsCommandSession(),
			new AppDriverOptions
			{
				AutomaticDiagnostics = new AutomaticDiagnosticsOptions
				{
					OutputDirectory = root,
					MaximumArtifactSizeBytes = 4096,
					FailureBufferSizeBytes = 2048,
					CaptureFinalScreenshotOnFailure = false,
					CaptureFinalTreeOnFailure = false,
				},
			});
		driver.MarkDiagnosticsFailure(new InvalidOperationException("budget failure"));
		driver.Dispose();

		var directory = driver.AutomaticDiagnosticsArtifactDirectory!;
		var totalBytes = Directory.EnumerateFiles(directory).Sum(static path => new FileInfo(path).Length);
		Assert.That(totalBytes, Is.LessThanOrEqualTo(4096));
	}

	[Test]
	public async Task ExplicitRecorderCompleteAsyncReportsCleanupFailureWhileDisposeRemainsSafe()
	{
		var path = Path.Combine(CreateCleanDirectory("explicit-cleanup-failure"), "trace.json");
		var session = new DiagnosticsCommandSession(throwWhenStopping: true);
		using var driver = AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe"),
			session);
		var recording = driver.StartSemanticRecording(path, new SemanticRecordingOptions
		{
			OutputFormat = SemanticRecordingOutputFormat.CompactJson,
		});

		var exception = Assert.ThrowsAsync<AppDriverException>(async () => await recording.CompleteAsync());
		Assert.That(exception!.Message, Does.Contain("stop failed"));
		Assert.DoesNotThrow(recording.Dispose);
	}

	[Test]
	public void RetentionRemovesExpiredDiagnosticSessions()
	{
		var root = CreateCleanDirectory("retention");
		var expired = Directory.CreateDirectory(Path.Combine(root, "dft-expired"));
		expired.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-3);
		var session = new DiagnosticsCommandSession();
		using (AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe"),
			session,
			new AppDriverOptions
			{
				AutomaticDiagnostics = new AutomaticDiagnosticsOptions
				{
					OutputDirectory = root,
					MaximumArtifactAge = TimeSpan.FromDays(1),
				},
			}))
		{
			Assert.That(Directory.Exists(expired.FullName), Is.False);
		}
	}

	[Test]
	public void AutomaticDiagnosticOptionsRejectInvalidArtifactBudgetsAtDriverCreation()
	{
		var options = new AppDriverOptions
		{
			AutomaticDiagnostics = new AutomaticDiagnosticsOptions
			{
				MaximumArtifactSizeBytes = 1024,
				FailureBufferSizeBytes = 2048,
			},
		};

		Assert.Throws<ArgumentOutOfRangeException>(() => AppDriver.CreateForTests(
			AppConnection.ForAttach(new FakeTargetProcess(), "diagnostics-pipe"),
			new DiagnosticsCommandSession(),
			options));
	}

	private static string[] ArtifactKinds(JObject manifest) =>
		(manifest["artifacts"] as JArray)?.Select(static artifact => (string?)artifact["kind"] ?? string.Empty).ToArray() ?? [];

	private static string CreateCleanDirectory(string name)
	{
		var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "automatic-diagnostics", name);
		if (Directory.Exists(path))
			Directory.Delete(path, recursive: true);
		Directory.CreateDirectory(path);
		return path;
	}

	private sealed class FakeArtifactSink(string resultsDirectory, string testName, bool hasFailed = false) : IDiagnosticsArtifactSink
	{
		public List<string> Attachments { get; } = [];

		public List<AppDriverDiagnostic> Logged { get; } = [];

		public DiagnosticsTestContext GetCurrentTestContext() => new()
		{
			ResultsDirectory = resultsDirectory,
			TestName = testName,
			HasFailed = hasFailed,
		};

		public void AttachArtifact(string path, string description) => Attachments.Add(path);

		public void Log(AppDriverDiagnostic diagnostic)
		{
			Logged.Add(diagnostic);
		}
	}

	private sealed class DiagnosticsCommandSession(bool throwWhenStopping = false, bool throwOnTree = false) : IUnsafeAppDriverCommandSession, IAppDriverStreamingSession
	{
		public StartSendingCommandRequest? StartRequest { get; private set; }

		public bool FrameRead { get; private set; }

		public TResponse Send<TResponse>(IpcCommand command)
		{
			object response = command switch
			{
				StopSendingCommandRequest stop when throwWhenStopping => throw new InvalidOperationException("stop failed"),
				StopSendingCommandRequest stop => new StopSendingCommandResponse(stop.SubscriptionId, ProtocolConstants.Statuses.Stopped),
				ScreenshotCommandRequest => new ScreenshotCommandResponse(Convert.ToBase64String([1, 2, 3, 4])),
				GetVisualTreeCommandRequest when throwOnTree => throw new InvalidOperationException("tree failed"),
				GetVisualTreeCommandRequest => VisualTreeSnapshot.Create(1, []),
				_ => throw new InvalidOperationException("Unexpected command " + command.Kind),
			};
			return (TResponse)response;
		}

		public IAppDriverStreamSession StartStream(StartSendingCommandRequest command, int timeoutMs)
		{
			StartRequest = command;
			return new DiagnosticsStreamSession(command, () => FrameRead = true);
		}
	}

	private sealed class DiagnosticsStreamSession(StartSendingCommandRequest command, Action onFrameRead) : IAppDriverStreamSession
	{
		private int readCount;

		public StartSendingCommandResponse Start { get; } = new("diagnostics-subscription", command.StreamKind, ProtocolConstants.Statuses.Started)
		{
			IntervalMs = command.IntervalMs,
		};

		public StreamMessage? ReadFrame(int timeoutMs, CancellationToken cancellationToken = default)
		{
			if (Interlocked.Increment(ref readCount) == 1)
			{
				onFrameRead();
				return new StreamMessage(Start.SubscriptionId, Start.StreamKind, 1, new SemanticRecordingBatch
				{
					RecordingId = "diagnostics-recording",
					BatchSequenceNumber = 1,
					Frames =
					[
						new SemanticRecordingFrame
						{
							RecordingId = "diagnostics-recording",
							FrameKind = "action",
							SequenceNumber = 1,
							Action = new RecordedInputAction { ActionKind = "click" },
						},
					],
				});
			}

			cancellationToken.WaitHandle.WaitOne(Math.Min(timeoutMs, 20));
			return null;
		}

		public void Dispose()
		{
		}
	}
}
