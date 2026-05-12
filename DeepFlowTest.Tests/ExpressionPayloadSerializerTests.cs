namespace DeepFlowTest.Tests;

using System;
using System.Linq;
using System.Linq.Expressions;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class ExpressionPayloadSerializerTests
{
	[Test]
	public void IdenticalExpressionAndClosureValuesProduceSameHash()
	{
		var first = CreatePayload("Submit", 2);
		var second = CreatePayload("Submit", 2);

		Assert.That(second.ExpressionHash, Is.EqualTo(first.ExpressionHash));
		Assert.That(first.ExpressionJson, Is.Not.Empty);
		Assert.That(first.ClosureValues.Values, Does.Contain("Submit"));
		Assert.That(first.ClosureValues.Values, Does.Contain(2));
	}

	[Test]
	public void ChangedClosureValueProducesDifferentHash()
	{
		var first = CreatePayload("Submit", 2);
		var second = CreatePayload("Cancel", 2);

		Assert.That(second.ExpressionHash, Is.Not.EqualTo(first.ExpressionHash));
	}

	[Test]
	public void HashSurvivesPayloadAndCommandRoundTrip()
	{
		var payload = CreatePayload("Submit", 2);
		var roundTrippedPayload = MessagePacker.ConvertTo<ExpressionMatcherPayload>(MessagePacker.Unpack(MessagePacker.Pack(payload)));

		Assert.That(roundTrippedPayload.ExpressionHash, Is.EqualTo(payload.ExpressionHash));
		Assert.That(roundTrippedPayload.ExpressionText, Is.EqualTo(payload.ExpressionText));

		var request = new FindElementCommandRequest
		{
			MatcherCode = payload,
			MatcherHash = payload.ExpressionHash,
		};
		var roundTrippedRequest = MessagePacker.ConvertTo<FindElementCommandRequest>(MessagePacker.Unpack(MessagePacker.Pack(request)));

		Assert.That(roundTrippedRequest.MatcherHash, Is.EqualTo(payload.ExpressionHash));
		var requestPayload = MessagePacker.ConvertTo<ExpressionMatcherPayload>(roundTrippedRequest.MatcherCode!);
		Assert.That(requestPayload.ExpressionHash, Is.EqualTo(payload.ExpressionHash));
	}

	private static ExpressionMatcherPayload CreatePayload(string expectedName, int minimumCount)
	{
		Expression<Func<MatcherTarget, bool>> matcher = target =>
			target.Name == expectedName && target.Count >= minimumCount && target.Tags.Contains(expectedName);
		return ExpressionPayloadSerializer.Serialize(matcher);
	}

	private sealed class MatcherTarget
	{
		public string Name { get; set; } = string.Empty;

		public int Count { get; set; }

		public string[] Tags { get; set; } = Array.Empty<string>();
	}
}
