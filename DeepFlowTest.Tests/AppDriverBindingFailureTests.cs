namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class AppDriverBindingFailureTests
{
	[Test]
	public void StrictModeCheckpointsAfterSuccessfulCommands()
	{
		var session = new BindingFailureSession();
		session.EnqueueCheckpoint(new BindingFailureBatchDto { LastSequenceNumber = 0 });
		session.EnqueueCommandResponse(new ScreenshotCommandResponse());
		session.EnqueueCheckpoint(new BindingFailureBatchDto
		{
			LastSequenceNumber = 1,
			Failures = new[]
			{
				new BindingFailureDto
				{
					SequenceNumber = 1,
					TimestampUtc = DateTimeOffset.UtcNow,
					Severity = BindingFailureSeverity.Error,
					Message = "System.Windows.Data Error: strict failure",
				},
			},
		});
		var options = new AppDriverOptions
		{
			FailOnBindingFailures = true,
			Timeout = TimeSpan.FromMilliseconds(100),
			BindingFailures = new BindingFailureOptions { AssertOnDispose = false },
		};
		using var driver = AppDriver.CreateForTests(AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"), session, options);

		var exception = Assert.Throws<AssertionException>(() => driver.CaptureScreenshot());

		Assert.That(exception!.Message, Does.Contain("WPF binding failures detected"));
		Assert.That(exception.Message, Does.Contain("strict failure"));
		Assert.That(session.SentCommands.Exists(static command => command is GetBindingFailuresCommandRequest), Is.True);
	}

	[Test]
	public void IgnoredFailuresRaiseEventsWithoutFailingStrictMode()
	{
		var session = new BindingFailureSession();
		session.EnqueueCheckpoint(new BindingFailureBatchDto { LastSequenceNumber = 0 });
		session.EnqueueCommandResponse(new ScreenshotCommandResponse());
		session.EnqueueCheckpoint(new BindingFailureBatchDto
		{
			LastSequenceNumber = 1,
			Failures = new[]
			{
				new BindingFailureDto
				{
					SequenceNumber = 1,
					TimestampUtc = DateTimeOffset.UtcNow,
					Severity = BindingFailureSeverity.Error,
					Message = "Known noisy binding",
				},
			},
		});
		var options = new AppDriverOptions
		{
			FailOnBindingFailures = true,
			Timeout = TimeSpan.FromMilliseconds(100),
			BindingFailures = new BindingFailureOptions
			{
				AssertOnDispose = false,
				Ignore = new[] { BindingFailureFilter.Contains("Known noisy") },
			},
		};
		using var driver = AppDriver.CreateForTests(AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"), session, options);
		BindingFailureEventArgs? received = null;
		driver.BindingFailureReceived += (_, args) => received = args;

		Assert.DoesNotThrow(() => driver.CaptureScreenshot());

		Assert.That(received, Is.Not.Null);
		Assert.That(received!.IsIgnored, Is.True);
		Assert.That(driver.GetObservedBindingFailures(), Is.Empty);
	}

	[Test]
	public void StrictModeRequiresStreamingSessionSupport()
	{
		var options = new AppDriverOptions
		{
			FailOnBindingFailures = true,
			Timeout = TimeSpan.FromMilliseconds(100),
			BindingFailures = new BindingFailureOptions { AssertOnDispose = false },
		};

		var exception = Assert.Throws<AppDriverException>(() =>
			AppDriver.CreateForTests(
				AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
				new NonStreamingSession(),
				options));

		Assert.That(exception!.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.UnsupportedProtocol));
	}

	private sealed class BindingFailureSession : IUnsafeAppDriverCommandSession, IAppDriverStreamingSession
	{
		private readonly Queue<object> commandResponses = new();
		private readonly Queue<BindingFailureBatchDto> checkpoints = new();

		public List<IpcCommand> SentCommands { get; } = [];

		public void EnqueueCommandResponse(object response) => commandResponses.Enqueue(response);

		public void EnqueueCheckpoint(BindingFailureBatchDto batch) => checkpoints.Enqueue(batch);

		public TResponse Send<TResponse>(IpcCommand command)
		{
			SentCommands.Add(command);
			if (command is GetBindingFailuresCommandRequest)
				return (TResponse)(object)checkpoints.Dequeue();
			if (command is StopSendingCommandRequest stop)
				return (TResponse)(object)new StopSendingCommandResponse { SubscriptionId = stop.SubscriptionId };

			return (TResponse)commandResponses.Dequeue();
		}

		public IAppDriverStreamSession StartStream(StartSendingCommandRequest command, int timeoutMs)
		{
			SentCommands.Add(command);
			return new EmptyStreamSession(command.StreamKind);
		}
	}

	private sealed class EmptyStreamSession : IAppDriverStreamSession
	{
		public EmptyStreamSession(string streamKind)
		{
			Start = new StartSendingCommandResponse
			{
				SubscriptionId = "binding-sub",
				StreamKind = streamKind,
				Status = ProtocolConstants.Statuses.Started,
			};
		}

		public StartSendingCommandResponse Start { get; }

		public StreamMessage? ReadFrame(int timeoutMs, CancellationToken cancellationToken = default)
		{
			Thread.Sleep(10);
			return null;
		}

		public void Dispose()
		{
		}
	}

	private sealed class NonStreamingSession : IUnsafeAppDriverCommandSession
	{
		public TResponse Send<TResponse>(IpcCommand command)
		{
			if (command is GetBindingFailuresCommandRequest)
				return (TResponse)(object)new BindingFailureBatchDto();

			return (TResponse)(object)StandardIpcResponse.Ok();
		}
	}

	private sealed class FakeTargetProcess : ITargetProcess
	{
		public int Id { get; set; } = 1234;

		public string ProcessName { get; set; } = "Target";

		public bool HasExited { get; set; }

		public int? ExitCode => HasExited ? 0 : null;

		public void Kill()
		{
		}

		public void Dispose()
		{
		}
	}
}
