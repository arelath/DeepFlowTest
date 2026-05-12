namespace DeepFlowTest.Tests;

using System;
using System.IO.Pipes;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
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

		Assert.That(
			() => client.Send(new HelloCommandRequest(), responseTimeoutMs: 25),
			Throws.TypeOf<NamedPipeSessionException>().With.Property(nameof(NamedPipeSessionException.ErrorCode)).EqualTo(ProtocolConstants.ErrorCodes.TargetExited));
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

	private static string UniquePipeName()
	{
		return $"deepflowtest-test-{Guid.NewGuid():N}";
	}
}
