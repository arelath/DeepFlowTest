namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using DeepFlowTest.AppDriverPayload.Streaming;
using DeepFlowTest.Contracts;
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
