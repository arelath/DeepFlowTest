namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Tests.Fakes;
using NUnit.Framework;

[TestFixture]
public sealed class NamedPipeSessionTests
{
	[Test]
	public async Task OneRequestResponseSucceeds()
	{
		var pipeName = UniquePipeName();
		var serverTask = Task.Run(() =>
		{
			using var server = new NamedPipeServer(pipeName);
			var command = server.WaitForNextCommand();
			command.Respond(StandardIpcResponse.Ok());
		});

		using var client = new NamedPipeClient(pipeName);
		var response = MessagePacker.ConvertTo<StandardIpcResponse>(await client.SendAsync(new HelloCommandRequest()));

		Assert.That(response.Success, Is.True);
		await serverTask;
	}

	[Test]
	public async Task ServerSendsOneResponseEvenIfCommandTriesTwice()
	{
		var pipeName = UniquePipeName();
		var serverTask = Task.Run(() =>
		{
			using var server = new NamedPipeServer(pipeName);
			var command = server.WaitForNextCommand();
			command.Respond(new StandardIpcResponse { Status = "first" });
			command.Respond(new StandardIpcResponse { Status = "second" });
		});

		using var client = new NamedPipeClient(pipeName);
		var response = MessagePacker.ConvertTo<StandardIpcResponse>(await client.SendAsync(new HelloCommandRequest()));

		Assert.That(response.Status, Is.EqualTo("first"));
		await serverTask;
	}

	[Test]
	public void ClientMapsMissingPipeToStableFailure()
	{
		var reinjectionRequests = 0;
		using var client = new NamedPipeClient(UniquePipeName(), requestReinjection: () => reinjectionRequests++, connectTimeoutMs: 25, connectRetryCount: 2);

		Assert.That(
			() => client.Send(new HelloCommandRequest(), responseTimeoutMs: 25),
			Throws.TypeOf<NamedPipeSessionException>().With.Property(nameof(NamedPipeSessionException.ErrorCode)).EqualTo(ProtocolConstants.ErrorCodes.ProtocolError));
		Assert.That(reinjectionRequests, Is.EqualTo(2));
	}

	[Test]
	public void ClientMapsTargetExitToStableFailure()
	{
		using var client = new NamedPipeClient(UniquePipeName(), getTargetExitCode: () => 42, connectTimeoutMs: 25);

		var exception = Assert.Throws<NamedPipeSessionException>(() => client.Send(new HelloCommandRequest(), responseTimeoutMs: 25));

		Assert.That(exception!.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.TargetExited));
		Assert.That(exception.TargetExitCode, Is.EqualTo(42));
		Assert.That(exception.Message, Does.Contain("42"));
	}

	[Test]
	public void ClientIncludesCrashLogWhenTargetExits()
	{
		using var client = new NamedPipeClient(
			UniquePipeName(),
			getTargetExitCode: () => -532462766,
			readTargetCrashLog: () => "System.InvalidOperationException: boom",
			connectTimeoutMs: 25);

		var exception = Assert.Throws<NamedPipeSessionException>(() => client.Send(new HelloCommandRequest(), responseTimeoutMs: 25));

		Assert.That(exception!.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.TargetExited));
		Assert.That(exception.TargetExitCode, Is.EqualTo(-532462766));
		Assert.That(exception.CrashLog, Does.Contain("boom"));
		Assert.That(exception.Message, Does.Contain("Last unhandled exception"));
		Assert.That(exception.Message, Does.Contain("boom"));
	}

	[Test]
	public void ClientRechecksTargetExitDuringConnectRetries()
	{
		var reinjectionRequests = 0;
		using var client = new NamedPipeClient(
			UniquePipeName(),
			getTargetExitCode: () => reinjectionRequests > 0 ? 99 : null,
			requestReinjection: () => reinjectionRequests++,
			connectTimeoutMs: 25,
			connectRetryCount: 2);

		var exception = Assert.Throws<NamedPipeSessionException>(() => client.Send(new HelloCommandRequest(), responseTimeoutMs: 25));

		Assert.That(exception!.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.TargetExited));
		Assert.That(exception.TargetExitCode, Is.EqualTo(99));
		Assert.That(reinjectionRequests, Is.EqualTo(1));
	}

	[Test]
	public void CommandSessionReportsRealTargetExitCode()
	{
		var process = new FakeTargetProcess { HasExited = true, ExitCode = 137 };
		using var connection = AppConnection.ForAttach(process, UniquePipeName(), "dotnet");
		using var session = new NamedPipeAppDriverCommandSession(connection, new AppDriverOptions { Timeout = TimeSpan.FromMilliseconds(25) });

		var exception = Assert.Throws<NamedPipeSessionException>(() => session.Send<HelloCommandResponse>(new HelloCommandRequest()));

		Assert.That(exception!.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.TargetExited));
		Assert.That(exception.TargetExitCode, Is.EqualTo(137));
		Assert.That(exception.Message, Does.Contain("137"));
		Assert.That(exception.Message, Does.Not.Contain("code 0"));
	}

	[Test]
	public void ClientMapsMalformedResponseToStableFailure()
	{
		var pipeName = UniquePipeName();
		var serverTask = Task.Run(() =>
		{
			using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
			pipe.WaitForConnection();
			_ = MessagePacker.ReadFrame(pipe);
			var invalidLength = BitConverter.GetBytes(-1);
			pipe.Write(invalidLength, 0, invalidLength.Length);
			pipe.Flush();
		});

		using var client = new NamedPipeClient(pipeName);
		Assert.That(
			() => client.Send(new HelloCommandRequest(), responseTimeoutMs: 250),
			Throws.TypeOf<NamedPipeSessionException>().With.Property(nameof(NamedPipeSessionException.ErrorCode)).EqualTo(ProtocolConstants.ErrorCodes.MalformedFrame));
		Assert.That(serverTask.Wait(TimeSpan.FromSeconds(2)), Is.True);
	}

	[Test]
	public void ClientMapsResponseTimeoutToStableFailure()
	{
		var pipeName = UniquePipeName();
		var releaseServer = new TaskCompletionSource<bool>();
		var serverTask = Task.Run(() =>
		{
			using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
			pipe.WaitForConnection();
			_ = MessagePacker.ReadFrame(pipe);
			releaseServer.Task.Wait(TimeSpan.FromSeconds(2));
		});

		using var client = new NamedPipeClient(pipeName);
		Assert.That(
			() => client.Send(new HelloCommandRequest(), responseTimeoutMs: 50),
			Throws.TypeOf<NamedPipeSessionException>().With.Property(nameof(NamedPipeSessionException.ErrorCode)).EqualTo(ProtocolConstants.ErrorCodes.CommandTimeout));

		releaseServer.SetResult(true);
		Assert.That(serverTask.Wait(TimeSpan.FromSeconds(2)), Is.True);
	}

	[Test]
	public async Task ClientRecreatesPipeAfterResponseTimeout()
	{
		var pipeName = UniquePipeName();
		var serverTask = Task.Run(() =>
		{
			using (var firstPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte))
			{
				firstPipe.WaitForConnection();
				_ = MessagePacker.ReadFrame(firstPipe);
				var frameAfterTimeout = MessagePacker.ReadFrame(firstPipe);
				if (frameAfterTimeout.HasFrame)
					throw new InvalidOperationException("The timed-out client did not disconnect its pipe.");
			}

			using var secondPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
			secondPipe.WaitForConnection();
			_ = MessagePacker.ReadFrame(secondPipe);
			MessagePacker.WriteFrame(secondPipe, StandardIpcResponse.Ok());
		});

		using var client = new NamedPipeClient(pipeName);
		Assert.That(
			async () => await client.SendAsync(new HelloCommandRequest(), responseTimeoutMs: 50),
			Throws.TypeOf<NamedPipeSessionException>().With.Property(nameof(NamedPipeSessionException.ErrorCode)).EqualTo(ProtocolConstants.ErrorCodes.CommandTimeout));

		var response = MessagePacker.ConvertTo<StandardIpcResponse>(
			await client.SendAsync(new HelloCommandRequest(), responseTimeoutMs: 2000));

		Assert.That(response.Success, Is.True);
		await serverTask;
	}

	[Test]
	public async Task ClientSerializesConcurrentRequestResponseExchanges()
	{
		var pipeName = UniquePipeName();
		var firstCommandReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseFirstResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var serverTask = Task.Run(() =>
		{
			using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
			pipe.WaitForConnection();
			var first = MessagePacker.ConvertTo<IpcCommand>(MessagePacker.ReadFrame(pipe).Message!);
			firstCommandReceived.SetResult(true);
			releaseFirstResponse.Task.GetAwaiter().GetResult();
			MessagePacker.WriteFrame(pipe, new StandardIpcResponse { Status = first.Kind });

			var second = MessagePacker.ConvertTo<IpcCommand>(MessagePacker.ReadFrame(pipe).Message!);
			MessagePacker.WriteFrame(pipe, new StandardIpcResponse { Status = second.Kind });
		});

		using var client = new NamedPipeClient(pipeName);
		var firstSend = client.SendAsync(new HelloCommandRequest(), responseTimeoutMs: 2000);
		await firstCommandReceived.Task;
		var secondSend = client.SendAsync(new PingCommandRequest(), responseTimeoutMs: 2000);
		releaseFirstResponse.SetResult(true);

		var firstResponse = MessagePacker.ConvertTo<StandardIpcResponse>(await firstSend);
		var secondResponse = MessagePacker.ConvertTo<StandardIpcResponse>(await secondSend);

		Assert.That(firstResponse.Status, Is.EqualTo(ProtocolConstants.Commands.Hello));
		Assert.That(secondResponse.Status, Is.EqualTo(ProtocolConstants.Commands.Ping));
		await serverTask;
	}

	[Test]
	public void ClientRechecksTargetExitAfterResponseTimeout()
	{
		var pipeName = UniquePipeName();
		var commandReceived = new TaskCompletionSource<bool>();
		var releaseServer = new TaskCompletionSource<bool>();
		var serverTask = Task.Run(() =>
		{
			using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
			pipe.WaitForConnection();
			_ = MessagePacker.ReadFrame(pipe);
			commandReceived.SetResult(true);
			releaseServer.Task.Wait(TimeSpan.FromSeconds(2));
		});

		using var client = new NamedPipeClient(pipeName, getTargetExitCode: () => commandReceived.Task.IsCompleted ? 42 : null);
		Assert.That(
			() => client.Send(new HelloCommandRequest(), responseTimeoutMs: 50),
			Throws.TypeOf<NamedPipeSessionException>().With.Property(nameof(NamedPipeSessionException.ErrorCode)).EqualTo(ProtocolConstants.ErrorCodes.TargetExited));

		releaseServer.SetResult(true);
		Assert.That(serverTask.Wait(TimeSpan.FromSeconds(2)), Is.True);
	}

	[Test]
	public async Task CommandSessionDoesNotMutateCallerCommandWhenApplyingDefaultTimeout()
	{
		var pipeName = UniquePipeName();
		var receivedTimeout = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var serverTask = Task.Run(() =>
		{
			using var server = new NamedPipeServer(pipeName);
			var receivedCommand = server.WaitForNextCommand();
			receivedTimeout.SetResult(MessagePacker.ConvertTo<IpcCommand>(receivedCommand.Value).TimeoutMs);
			receivedCommand.Respond(new HelloCommandResponse());
		});
		var command = new HelloCommandRequest();
		using var connection = AppConnection.ForAttach(new FakeTargetProcess(), pipeName, "dotnet");
		using var session = new NamedPipeAppDriverCommandSession(
			connection,
			new AppDriverOptions { Timeout = TimeSpan.FromMilliseconds(250) });

		_ = session.Send<HelloCommandResponse>(command);

		Assert.That(command.TimeoutMs, Is.Null);
		Assert.That(await receivedTimeout.Task, Is.EqualTo(250));
		await serverTask;
	}

	[Test]
	public async Task ExistingCommandSessionUsesRuntimeTimeoutChanges()
	{
		var pipeName = UniquePipeName();
		var receivedTimeouts = new List<int?>();
		var serverTask = Task.Run(() =>
		{
			using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
			pipe.WaitForConnection();
			var hello = MessagePacker.ConvertTo<IpcCommand>(MessagePacker.ReadFrame(pipe).Message!);
			MessagePacker.WriteFrame(pipe, new HelloCommandResponse
			{
				ProtocolVersion = ProtocolConstants.ProtocolVersion,
				IsReusable = true,
				ControlConnectionMode = ProtocolConstants.ControlConnectionModes.PersistentSerialized,
			});
			for (var index = 0; index < 2; index++)
			{
				var receivedCommand = MessagePacker.ReadFrame(pipe);
				receivedTimeouts.Add(MessagePacker.ConvertTo<IpcCommand>(receivedCommand.Message!).TimeoutMs);
				MessagePacker.WriteFrame(pipe, new HelloCommandResponse());
			}
			return hello.Kind;
		});
		var options = new AppDriverOptions { Timeout = TimeSpan.FromMilliseconds(250) };
		using var connection = AppConnection.ForAttach(new FakeTargetProcess(), pipeName, "dotnet");
		using var session = new NamedPipeAppDriverCommandSession(connection, options);

		_ = session.NegotiateControlConnection();
		_ = session.Send<HelloCommandResponse>(new HelloCommandRequest());
		options.Timeout = TimeSpan.FromMilliseconds(875);
		_ = session.Send<HelloCommandResponse>(new HelloCommandRequest());

		Assert.That(await serverTask, Is.EqualTo(ProtocolConstants.Commands.Hello));
		Assert.That(receivedTimeouts, Is.EqualTo(new int?[] { 250, 875 }));
	}

	[Test]
	public async Task NegotiatedOneShotSessionUsesANewConnectionForTheNextCommand()
	{
		var pipeName = UniquePipeName();
		var serverTask = Task.Run(() =>
		{
			var kinds = new List<string>();
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

			using var commandPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
			commandPipe.WaitForConnection();
			kinds.Add(MessagePacker.ConvertTo<IpcCommand>(MessagePacker.ReadFrame(commandPipe).Message!).Kind);
			MessagePacker.WriteFrame(commandPipe, new PingCommandResponse { ProcessId = 42 });
			return kinds;
		});
		using var connection = AppConnection.ForAttach(new FakeTargetProcess(), pipeName, "dotnet");
		using var session = new NamedPipeAppDriverCommandSession(
			connection,
			new AppDriverOptions { Timeout = TimeSpan.FromMilliseconds(2000) });

		_ = session.NegotiateControlConnection();
		var ping = session.Send<PingCommandResponse>(new PingCommandRequest());

		Assert.That(ping.ProcessId, Is.EqualTo(42));
		Assert.That(await serverTask, Is.EqualTo(new[] { ProtocolConstants.Commands.Hello, ProtocolConstants.Commands.Ping }));
	}

	private static string UniquePipeName()
	{
		return $"deepflowtest-test-{Guid.NewGuid():N}";
	}
}
