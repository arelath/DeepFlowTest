namespace DeepFlowTest.Cli.Tests;

using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class StreamCommandHandlerTests
{
	[TestCase(ProtocolConstants.StreamKinds.VisualTree, "visual-tree")]
	[TestCase(ProtocolConstants.StreamKinds.VisualTreeDelta, "visual-tree-delta")]
	[TestCase(ProtocolConstants.StreamKinds.Screenshot, "screenshot")]
	[TestCase(ProtocolConstants.StreamKinds.EventLog, "event-log")]
	public void StreamKindCommandStartsFramesAndStops(string streamKind, string commandName)
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "stream", commandName, "--pid", "1234", "--duration-ms", "120", "--interval-ms", "60" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"status\":\"started\""));
		Assert.That(result.Stdout, Does.Contain("\"messageKind\":\"stream\""));
		Assert.That(result.Stdout, Does.Contain("\"status\":\"stopped\""));
		Assert.That(session.Session.Commands.OfType<StartSendingCommandRequest>().Single().StreamKind, Is.EqualTo(streamKind));
		Assert.That(session.Session.Commands.OfType<StopSendingCommandRequest>().Single().SubscriptionId, Is.EqualTo("sub-1"));
	}

	[Test]
	public void StreamRejectsInvalidIntervalAndFormat()
	{
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: new FakeAppSessionService());

		var badInterval = CliTestHost.Run(new[] { "stream", "visual-tree", "--pid", "1234", "--interval-ms", "10" }, services);
		var badFormat = CliTestHost.Run(new[] { "stream", "screenshot", "--pid", "1234", "--image-format", "tiff" }, services);

		Assert.That(badInterval.ExitCode, Is.EqualTo(1));
		Assert.That(badInterval.Stdout, Does.Contain("\"code\":\"invalid-arguments\""));
		Assert.That(badFormat.ExitCode, Is.EqualTo(1));
	}

	[Test]
	public void StreamDtosRoundTripThroughMessagePacker()
	{
		foreach (var kind in new[]
		{
			ProtocolConstants.StreamKinds.VisualTree,
			ProtocolConstants.StreamKinds.VisualTreeDelta,
			ProtocolConstants.StreamKinds.Screenshot,
			ProtocolConstants.StreamKinds.EventLog,
		})
		{
			var request = new StartSendingCommandRequest
			{
				StreamKind = kind,
				IntervalMs = 100,
				TargetId = "target",
				PropNames = new[] { "Name" },
				Format = "png",
			};
			var unpacked = MessagePacker.ConvertTo<StartSendingCommandRequest>(MessagePacker.Unpack(MessagePacker.Pack(request)));
			Assert.That(unpacked.StreamKind, Is.EqualTo(kind));
			Assert.That(unpacked.PropNames, Is.EqualTo(new[] { "Name" }));
		}

		var message = new StreamMessage
		{
			SubscriptionId = "sub",
			StreamKind = ProtocolConstants.StreamKinds.EventLog,
			SequenceNumber = 2,
			Data = new { status = "heartbeat" },
		};
		var error = new StreamMessage
		{
			SubscriptionId = "sub",
			StreamKind = ProtocolConstants.StreamKinds.EventLog,
			SequenceNumber = 3,
			Error = new CliStreamError { Code = "bad", Message = "broken" },
		};
		var unpackedMessage = MessagePacker.ConvertTo<StreamMessage>(MessagePacker.Unpack(MessagePacker.Pack(message)));
		var unpackedError = MessagePacker.ConvertTo<StreamMessage>(MessagePacker.Unpack(MessagePacker.Pack(error)));

		Assert.That(unpackedMessage.SequenceNumber, Is.EqualTo(2));
		Assert.That(unpackedError.Error!.Code, Is.EqualTo("bad"));
	}
}
