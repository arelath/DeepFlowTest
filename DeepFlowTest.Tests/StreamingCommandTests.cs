namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using DeepFlowTest.AppDriverPayload.Streaming;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

[TestFixture]
public sealed class StreamingCommandTests
{
	[Test]
	public void SubscriptionEmitsErrorFrameWhenCaptureFails()
	{
		var sent = new List<StreamMessage>();
		using var subscription = new DelegateStreamSubscription(
			"sub",
			ProtocolConstants.StreamKinds.EventLog,
			"connection",
			50,
			message =>
			{
				sent.Add((StreamMessage)message);
				return true;
			},
			_ => throw new InvalidOperationException("capture failed"));

		subscription.Start();
		Assert.That(SpinWaitUntil(() => sent.Exists(static frame => frame.Error is not null), TimeSpan.FromSeconds(2)), Is.True);
		subscription.Stop();

		Assert.That(sent[0].Error!.Code, Is.EqualTo(ProtocolConstants.ErrorCodes.ProtocolError));
		Assert.That(sent[0].Error!.Message, Does.Contain("capture failed"));
	}

	[Test]
	public void StreamMessageExposesCompatFrameAliases()
	{
		var frame = new StreamMessage("subscription", ProtocolConstants.StreamKinds.Screenshot, 42, new ScreenshotCommandResponse("AQID"));

		Assert.That(frame.Kind, Is.EqualTo(ProtocolConstants.StreamKinds.Screenshot));
		Assert.That(frame.StreamKind, Is.EqualTo(ProtocolConstants.StreamKinds.Screenshot));
		Assert.That(frame.Sequence, Is.EqualTo(42));
		Assert.That(frame.SequenceNumber, Is.EqualTo(42));
		Assert.That(((ScreenshotCommandResponse)frame.Data!).Base64Screenshot, Is.EqualTo("AQID"));
	}

	[Test]
	public void VisualTreeDeltaPayloadsUseCompatShapes()
	{
		var previous = VisualTreeSnapshot.Create(1, new[]
		{
			new VisualTreeNodeDto { TargetId = "root", TypeName = "Window", IsRoot = true },
		});
		var current = VisualTreeSnapshot.Create(2, new[]
		{
			new VisualTreeNodeDto { TargetId = "root", TypeName = "Window", IsRoot = true },
			new VisualTreeNodeDto { TargetId = "button", TypeName = "Button", ParentId = "root" },
		});
		var fullFrame = new StreamMessage("subscription", ProtocolConstants.StreamKinds.VisualTreeDelta, 1, new VisualTreeDeltaSnapshotFrame(previous));
		var deltaFrame = new StreamMessage("subscription", ProtocolConstants.StreamKinds.VisualTreeDelta, 2, VisualTreeSnapshotDelta.Create(previous, current));

		var fullRoundTrip = MessagePacker.ConvertTo<StreamMessage>(MessagePacker.Unpack(MessagePacker.Pack(fullFrame)));
		var deltaRoundTrip = MessagePacker.ConvertTo<StreamMessage>(MessagePacker.Unpack(MessagePacker.Pack(deltaFrame)));
		var fullData = JObject.FromObject(fullRoundTrip.Data!);
		var deltaData = JObject.FromObject(deltaRoundTrip.Data!);

		Assert.That(fullData.Value<bool>("IsDelta"), Is.False);
		Assert.That(fullData["Snapshot"], Is.Not.Null);
		Assert.That(deltaData["BaseSequenceNumber"], Is.Not.Null);
		Assert.That(deltaData["CurrentSequenceNumber"], Is.Not.Null);
	}

	private static bool SpinWaitUntil(Func<bool> condition, TimeSpan timeout)
	{
		var stop = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < stop)
		{
			if (condition())
				return true;
			System.Threading.Thread.Sleep(10);
		}

		return false;
	}
}
