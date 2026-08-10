namespace DeepFlowTest.Tests;

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Tests.Fakes;
using NUnit.Framework;
using FakeSession = DeepFlowTest.Tests.Fakes.FakeAppDriverCommandSession;

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
	public void CapturedClosureValuesDeserializeAsConstants()
	{
		var criteria = new MatcherCriteria("Submit", 2);
		Expression<Func<MatcherTarget, bool>> matcher = target =>
			target.Name == criteria.ExpectedName
			&& target.Count >= criteria.MinimumCount
			&& target.Tags.Contains(criteria.ExpectedName);

		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		var deserialized = (Expression<Func<MatcherTarget, bool>>)ExpressionPayloadSerializer.Deserialize(payload.ExpressionJson);
		var predicate = deserialized.Compile();

		Assert.That(predicate(new MatcherTarget { Name = "Submit", Count = 3, Tags = new[] { "Submit" } }), Is.True);
		Assert.That(predicate(new MatcherTarget { Name = "Cancel", Count = 3, Tags = new[] { "Submit" } }), Is.False);
		Assert.That(predicate(new MatcherTarget { Name = "Submit", Count = 1, Tags = new[] { "Submit" } }), Is.False);
	}

	[Test]
	public void DiagnosticTextPrintsCapturedClosureValuesAsConstants()
	{
		var expectedName = "Submit";
		var minimumCount = 2;
		Expression<Func<MatcherTarget, bool>> matcher = target =>
			target.Name == expectedName && target.Count >= minimumCount;

		var text = ExpressionPayloadSerializer.FormatDiagnosticText(matcher);

		Assert.That(text, Does.Contain("\"Submit\""));
		Assert.That(text, Does.Contain("2"));
		Assert.That(text, Does.Not.Contain("DisplayClass"));
		Assert.That(text, Does.Not.Contain("value("));
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

	[Test]
	public void PublicExpressionGetElementsSendsMatcherPayload()
	{
		var session = new FakeSession(new FindElementCommandResponse
		{
			Matches =
			{
				new FindElementMatchResponse { TargetId = "target", TypeName = "Button" },
			},
			MatchCount = 1,
		});
		var driver = DeepFlowTest.AppDriver.CreateForTests(
			DeepFlowTest.AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session);
		Expression<Func<VisualTreeNodeDto, bool>> matcher = node => node.TypeName == "Button";

		var elements = driver.GetElements(matcher);

		Assert.That(elements.Single().TargetId, Is.EqualTo("target"));
		var command = session.SentCommands.OfType<FindElementCommandRequest>().Single();
		Assert.That(command.MatcherHash, Is.Not.Empty);
		Assert.That(command.MatcherCode, Is.Not.Null);
	}

	[Test]
	public void PublicElementExpressionGetElementsSendsEvalEnvelopeAndCollectedProperties()
	{
		var session = new FakeSession(new FindElementCommandResponse
		{
			Status = ProtocolConstants.Statuses.Ok,
			Matches =
			{
				new FindElementMatchResponse { TargetId = "target", TypeName = "Button", Properties = { ["Name"] = "Run" } },
			},
			MatchCount = 1,
		});
		var driver = DeepFlowTest.AppDriver.CreateForTests(
			DeepFlowTest.AppConnection.ForAttach(new FakeTargetProcess(), "test-pipe"),
			session);

		var elements = driver.GetElements(element => element["Name"] == "Run", timeout: TimeSpan.FromMilliseconds(1));

		Assert.That(elements.Single().TargetId, Is.EqualTo("target"));
		var command = session.SentCommands.OfType<FindElementCommandRequest>().Single();
		Assert.That(command.MatcherCode, Is.TypeOf<Eval>());
		Assert.That(((Eval)command.MatcherCode!).Type, Is.EqualTo(Eval.EvalType));
		Assert.That(command.MatcherHash, Is.Not.Empty);
		Assert.That(command.PropNames, Does.Contain("Name"));
	}

	[Test]
	public void SyncOverAsyncExpressionsAreBlockedByDefaultAndCanBeAllowed()
	{
		Expression<Func<MatcherTarget, int>> unsafeExpression = _ => Task.FromResult(5).Result;

		var exception = Assert.Throws<InvalidOperationException>(() => ExpressionPayloadSerializer.Serialize(unsafeExpression));

		Assert.That(exception!.Message, Does.Contain("Task.Result"));

		try
		{
			ExpressionPayloadOptions.AllowUnsafeSyncOverAsync = true;
			Assert.That(ExpressionPayloadSerializer.Serialize(unsafeExpression).ExpressionJson, Is.Not.Empty);
		}
		finally
		{
			ExpressionPayloadOptions.AllowUnsafeSyncOverAsync = false;
		}
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

		public string[] Tags { get; set; } = [];
	}

	private sealed record MatcherCriteria(string ExpectedName, int MinimumCount);
}
