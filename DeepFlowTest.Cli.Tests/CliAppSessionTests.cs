namespace DeepFlowTest.Cli.Tests;

using System;
using System.IO.Pipes;
using System.Threading.Tasks;
using DeepFlowTest;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class CliAppSessionTests
{
	[Test]
	public void ExistingPipeIsReused()
	{
		var connector = new FakeConnector { SucceedOnAttempt = 1 };
		var injector = new FakeInjector();
		var service = new AutomationSessionService(connector, _ => injector);

		using var session = service.Open(Target(), new AutomationAttachOptions());

		Assert.That(session, Is.Not.Null);
		Assert.That(connector.Attempts, Is.EqualTo(1));
		Assert.That(injector.InjectCount, Is.EqualTo(0));
	}

	[Test]
	public void MissingPipeWithNoInjectReturnsPipeFailed()
	{
		var connector = new FakeConnector { SucceedOnAttempt = int.MaxValue };
		var service = new AutomationSessionService(connector, _ => new FakeInjector());

		var ex = Assert.Throws<AutomationException>(() => service.Open(Target(), new AutomationAttachOptions { NoInject = true }));

		Assert.That(ex!.ErrorCode, Is.EqualTo(AutomationErrorCodes.PipeFailed));
	}

	[Test]
	public void MissingPipeWithInjectionAllowedInvokesInjector()
	{
		var connector = new FakeConnector { SucceedOnAttempt = 2 };
		var injector = new FakeInjector();
		var service = new AutomationSessionService(connector, _ => injector);

		using var session = service.Open(Target(), new AutomationAttachOptions());

		Assert.That(session, Is.Not.Null);
		Assert.That(connector.Attempts, Is.EqualTo(2));
		Assert.That(injector.InjectCount, Is.EqualTo(1));
	}

	[Test]
	public void PostInjectionConnectRetriesWithinTimeout()
	{
		var connector = new FakeConnector { SucceedOnAttempt = 4 };
		var injector = new FakeInjector();
		var service = new AutomationSessionService(connector, _ => injector);

		using var session = service.Open(Target(), new AutomationAttachOptions { TimeoutMs = 500 });

		Assert.That(session, Is.Not.Null);
		Assert.That(connector.Attempts, Is.EqualTo(4));
		Assert.That(injector.InjectCount, Is.EqualTo(1));
	}

	[Test]
	public void ProtocolMismatchMapsToProtocolError()
	{
		var connector = new FakeConnector { ProtocolMismatch = true };
		var injector = new FakeInjector();
		var service = new AutomationSessionService(connector, _ => injector);

		var ex = Assert.Throws<AutomationException>(() => service.Open(Target(), new AutomationAttachOptions()));

		Assert.That(ex!.ErrorCode, Is.EqualTo(AutomationErrorCodes.ProtocolError));
		Assert.That(injector.InjectCount, Is.EqualTo(0));
	}

	[Test]
	public void DisposalDoesNotKillAttachedTargetProcess()
	{
		var process = new FakeTargetProcess { Id = 55 };
		var connector = new FakeConnector { SucceedOnAttempt = 1 };
		var service = new AutomationSessionService(connector, _ => new FakeInjector());

		service.Open(Target(process), new AutomationAttachOptions()).Dispose();

		Assert.That(process.Killed, Is.False);
	}

	[Test]
	public async Task ConnectorHelloAndCommandReuseOneControlConnection()
	{
		var pipeName = $"deepflowtest-cli-session-{Guid.NewGuid():N}";
		var serverTask = Task.Run(() =>
		{
			using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
			pipe.WaitForConnection();
			var hello = MessagePacker.ConvertTo<IpcCommand>(MessagePacker.ReadFrame(pipe).Message!);
			MessagePacker.WriteFrame(pipe, new HelloCommandResponse
			{
				ProtocolVersion = ProtocolConstants.ProtocolVersion,
				PipeName = pipeName,
				IsReusable = true,
				ControlConnectionMode = ProtocolConstants.ControlConnectionModes.PersistentSerialized,
			});
			var ping = MessagePacker.ConvertTo<IpcCommand>(MessagePacker.ReadFrame(pipe).Message!);
			MessagePacker.WriteFrame(pipe, new PingCommandResponse { ProcessId = 42 });
			return new[] { hello.Kind, ping.Kind };
		});

		using var connection = AppConnection.ForAttach(new FakeTargetProcess { Id = 42 }, pipeName, "dotnet");
		var connector = new NamedPipeAutomationSessionConnector();
		Assert.That(connector.TryConnect(connection, 2000, out var session, out var error), Is.True, error?.Message);
		using (session)
		{
			var ping = session!.Send<PingCommandResponse>(new PingCommandRequest(), 2000);
			Assert.That(ping.ProcessId, Is.EqualTo(42));
		}

		Assert.That(await serverTask, Is.EqualTo(new[] { ProtocolConstants.Commands.Hello, ProtocolConstants.Commands.Ping }));
	}

	[Test]
	public async Task ConnectorFallsBackWhenHelloAdvertisesOneShotConnections()
	{
		var pipeName = $"deepflowtest-cli-session-{Guid.NewGuid():N}";
		var serverTask = Task.Run(() =>
		{
			var kinds = new System.Collections.Generic.List<string>();
			using (var helloPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte))
			{
				helloPipe.WaitForConnection();
				kinds.Add(MessagePacker.ConvertTo<IpcCommand>(MessagePacker.ReadFrame(helloPipe).Message!).Kind);
				MessagePacker.WriteFrame(helloPipe, new HelloCommandResponse
				{
					ProtocolVersion = ProtocolConstants.ProtocolVersion,
					ControlConnectionMode = ProtocolConstants.ControlConnectionModes.OneShot,
				});
			}

			using var pingPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
			pingPipe.WaitForConnection();
			kinds.Add(MessagePacker.ConvertTo<IpcCommand>(MessagePacker.ReadFrame(pingPipe).Message!).Kind);
			MessagePacker.WriteFrame(pingPipe, new PingCommandResponse { ProcessId = 42 });
			return kinds;
		});

		using var connection = AppConnection.ForAttach(new FakeTargetProcess { Id = 42 }, pipeName, "dotnet");
		var connector = new NamedPipeAutomationSessionConnector();
		Assert.That(connector.TryConnect(connection, 2000, out var session, out var error), Is.True, error?.Message);
		using (session)
		{
			var ping = session!.Send<PingCommandResponse>(new PingCommandRequest(), 2000);
			Assert.That(ping.ProcessId, Is.EqualTo(42));
		}

		Assert.That(await serverTask, Is.EqualTo(new[] { ProtocolConstants.Commands.Hello, ProtocolConstants.Commands.Ping }));
	}

	private static TargetInfo Target(FakeTargetProcess? process = null) =>
		new()
		{
			ProcessId = process?.Id ?? 42,
			ProcessName = "Target",
			TargetProcess = process ?? new FakeTargetProcess { Id = 42 },
		};

	private sealed class FakeConnector : IAutomationSessionConnector
	{
		public int Attempts { get; private set; }

		public int SucceedOnAttempt { get; set; } = int.MaxValue;

		public bool ProtocolMismatch { get; set; }

		public bool TryConnect(AppConnection connection, int timeoutMs, out IAutomationSession? session, out AutomationException? error)
		{
			Attempts++;
			session = null;
			error = null;
			if (ProtocolMismatch)
			{
				error = new AutomationException(AutomationErrorCodes.ProtocolError, "protocol mismatch");
				return false;
			}

			if (Attempts < SucceedOnAttempt)
			{
				error = new AutomationException(AutomationErrorCodes.PipeFailed, "missing pipe");
				return false;
			}

			session = new FakeCliAppSession
			{
				Hello = new HelloCommandResponse
				{
					ProtocolVersion = ProtocolConstants.ProtocolVersion,
					PipeName = connection.PipeName,
					ProcessId = connection.TargetProcess.Id,
					IsReusable = true,
				},
			};
			return true;
		}
	}

	private sealed class FakeInjector : IAppConnectionInjector
	{
		public int InjectCount { get; private set; }

		public AppConnectionInjectionResult Inject(AppConnection connection)
		{
			InjectCount++;
			return new AppConnectionInjectionResult();
		}

		public string? TryReadStartupLog(AppConnection connection) => null;
	}
}
