namespace DeepFlowTest.Tests;

using System;
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
		var session = new NamedPipeAppDriverCommandSession(connection, new AppDriverOptions { Timeout = TimeSpan.FromMilliseconds(25) });

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

	private static string UniquePipeName()
	{
		return $"deepflowtest-test-{Guid.NewGuid():N}";
	}
}
