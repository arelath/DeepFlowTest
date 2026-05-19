namespace DeepFlowTest.Cli.Tests;

using System.IO;
using System.Linq;
using DeepFlowTest;
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
	[TestCase(ProtocolConstants.StreamKinds.BindingFailures, "binding-failures")]
	[TestCase(ProtocolConstants.StreamKinds.SemanticRecording, "semantic-recording")]
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
	public void ScreenshotStreamFramesPreserveImagePayloadContract()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "stream", "screenshot", "--pid", "1234", "--duration-ms", "60", "--interval-ms", "60", "--target", "button-0002", "--image-format", "jpg" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"streamKind\":\"screenshot\""));
		Assert.That(result.Stdout, Does.Contain("\"targetId\":\"button-0002\""));
		Assert.That(result.Stdout, Does.Contain("\"format\":\"jpeg\""));
		Assert.That(result.Stdout, Does.Contain("\"byteCount\":4"));
		Assert.That(result.Stdout, Does.Contain("\"bytesBase64\":\"AQIDBA==\""));
		var start = session.Session.Commands.OfType<StartSendingCommandRequest>().Single();
		Assert.That(start.TargetId, Is.EqualTo("button-0002"));
		Assert.That(start.Format, Is.EqualTo(ImageFormat.Jpeg));
	}

	[Test]
	public void DurationZeroStreamsUntilCanceledOrSourceEnds()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);

		var result = CliTestHost.Run(new[] { "stream", "visual-tree", "--pid", "1234", "--interval-ms", "60" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout.Split("\"messageKind\":\"stream\"").Length - 1, Is.EqualTo(3));
		Assert.That(session.Session.Commands.OfType<StartSendingCommandRequest>().Single().IntervalMs, Is.EqualTo(60));
		Assert.That(session.Session.Commands.OfType<StopSendingCommandRequest>().Single().SubscriptionId, Is.EqualTo("sub-1"));
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
			ProtocolConstants.StreamKinds.BindingFailures,
			ProtocolConstants.StreamKinds.SemanticRecording,
		})
		{
			var request = new StartSendingCommandRequest
			{
				StreamKind = kind,
				IntervalMs = 100,
				TargetId = "target",
				PropNames = new[] { KnownProperties.Name },
				Format = ImageFormat.Png,
				SemanticRecording = kind == ProtocolConstants.StreamKinds.SemanticRecording
					? new SemanticRecordingOptionsDto { TextIdleMs = 250, MaxBatchFrames = 10 }
					: null,
			};
			var unpacked = MessagePacker.ConvertTo<StartSendingCommandRequest>(MessagePacker.Unpack(MessagePacker.Pack(request)));
			Assert.That(unpacked.StreamKind, Is.EqualTo(kind));
			Assert.That(unpacked.PropNames, Is.EqualTo(new[] { KnownProperties.Name }));
			if (kind == ProtocolConstants.StreamKinds.SemanticRecording)
				Assert.That(unpacked.SemanticRecording!.TextIdleMs, Is.EqualTo(250));
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

	[Test]
	public void RecordSemanticWritesJsonlFramesAndStops()
	{
		var session = new FakeAppSessionService();
		var services = CliTestHost.CreateServices(targetResolver: new FakeTargetResolver(), appSessionService: session);
		var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "semantic-recording.jsonl");
		if (File.Exists(path))
			File.Delete(path);

		var result = CliTestHost.Run(new[] { "record", "semantic", "--pid", "1234", "--out", path, "--duration-ms", "120", "--interval-ms", "60", "--text-idle-ms", "200" }, services);

		Assert.That(result.ExitCode, Is.EqualTo(0));
		Assert.That(result.Stdout, Does.Contain("\"framesWritten\""));
		Assert.That(File.Exists(path), Is.True);
		Assert.That(File.ReadAllText(path), Does.Contain("\"frameKind\":\"recording-started\""));
		var start = session.Session.Commands.OfType<StartSendingCommandRequest>().Single();
		Assert.That(start.StreamKind, Is.EqualTo(ProtocolConstants.StreamKinds.SemanticRecording));
		Assert.That(start.SemanticRecording!.TextIdleMs, Is.EqualTo(200));
		Assert.That(session.Session.Commands.OfType<StopSendingCommandRequest>().Single().SubscriptionId, Is.EqualTo("sub-1"));
	}
}
