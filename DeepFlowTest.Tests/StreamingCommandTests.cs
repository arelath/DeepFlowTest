namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using DeepFlowTest.AppDriverPayload;
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
	[Apartment(ApartmentState.STA)]
	public void VisualTreeDeltaStreamUsesCompatPayloadShapes()
	{
		_ = Application.Current ?? new Application();
		var window = new Window
		{
			Content = new Button { Name = "streamButton", Content = "Stream" },
			Width = 120,
			Height = 80,
			ShowInTaskbar = false,
			WindowStartupLocation = WindowStartupLocation.Manual,
			Left = -20000,
			Top = -20000,
		};
		var sent = new List<StreamMessage>();
		object? response = null;
		var responseCount = 0;
		var command = new NamedPipeServer.Command
		{
			Value = new StartSendingCommandRequest
			{
				StreamKind = ProtocolConstants.StreamKinds.VisualTreeDelta,
				IntervalMs = 50,
			},
			Respond = value =>
			{
				response = value;
				responseCount++;
			},
			CheckHasResponded = () => responseCount != 0,
			HoldConnectionOpen = () => { },
			TrySend = value =>
			{
				sent.Add((StreamMessage)value);
				return sent.Count < 2;
			},
		};
		var reusableSession = new ReusablePipeSession("stream-test", _ => { });
		var options = new AppDriverPayloadStartupOptions
		{
			PipeName = "stream-test",
			Mode = PayloadStartupModes.ReusableCli,
			PayloadRoot = AppContext.BaseDirectory,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};

		try
		{
			window.Show();
			PayloadLog.Initialize($"deepflowtest-test-{Guid.NewGuid():N}");
			AppDriverCommandDispatcher.Process(command, options, reusableSession);
			Assert.That(response, Is.TypeOf<StartSendingCommandResponse>());
			var start = (StartSendingCommandResponse)response!;

			Assert.That(SpinWaitUntil(() => sent.Count >= 2, TimeSpan.FromSeconds(3)), Is.True);
			Assert.That(reusableSession.StopSubscription(start.SubscriptionId), Is.True);

			var firstFrame = MessagePacker.ConvertTo<StreamMessage>(MessagePacker.Unpack(MessagePacker.Pack(sent[0])));
			var secondFrame = MessagePacker.ConvertTo<StreamMessage>(MessagePacker.Unpack(MessagePacker.Pack(sent[1])));
			var firstData = (JObject)firstFrame.Data!;
			var secondData = (JObject)secondFrame.Data!;

			Assert.That(firstData.Value<bool>("IsDelta"), Is.False);
			Assert.That(firstData["Snapshot"], Is.Not.Null);
			Assert.That(secondData["BaseSequenceNumber"], Is.Not.Null);
			Assert.That(secondData["CurrentSequenceNumber"], Is.Not.Null);
		}
		finally
		{
			if (response is StartSendingCommandResponse start)
				reusableSession.StopSubscription(start.SubscriptionId);
			window.Close();
		}
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
