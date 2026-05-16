namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class ExpressionPayloadSerializerEdgeCaseTests
{
	private static readonly DateTime StableDate = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
	private static readonly DateTimeOffset StableDateOffset = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

	[TestCaseSource(nameof(RoundTripCases))]
	public void ExpressionRoundTripsWithEquivalentBehavior(ExpressionCase testCase)
	{
		var expression = testCase.CreateExpression();
		var original = expression.Compile();
		var payload = ExpressionPayloadSerializer.Serialize(expression);
		var deserialized = (Expression<Func<MatcherTarget, bool>>)ExpressionPayloadSerializer.Deserialize(payload.ExpressionJson);
		var roundTripped = deserialized.Compile();

		Assert.That(roundTripped(testCase.MatchingTarget), Is.EqualTo(original(testCase.MatchingTarget)), testCase.Name);
		Assert.That(roundTripped(testCase.NonMatchingTarget), Is.EqualTo(original(testCase.NonMatchingTarget)), testCase.Name);
		Assert.That(payload.ExpressionHash, Is.Not.Empty, testCase.Name);
		Assert.That(payload.ExpressionJson, Is.Not.Empty, testCase.Name);
	}

	[Test]
	public void DiagnosticTextInlinesClosureMemberChains()
	{
		var criteria = new MatcherCriteria("Submit", 2, ["Submit"], TargetKind.Button, new NestedCriteria("Primary"));
		Expression<Func<MatcherTarget, bool>> expression = target =>
			target.Name == criteria.ExpectedName
			&& target.Count >= criteria.MinimumCount
			&& target.Metadata.Category == criteria.Nested.Category;

		var text = ExpressionPayloadSerializer.FormatDiagnosticText(expression);

		Assert.That(text, Does.Contain("\"Submit\""));
		Assert.That(text, Does.Contain("2"));
		Assert.That(text, Does.Contain("\"Primary\""));
		Assert.That(text, Does.Not.Contain("DisplayClass"));
		Assert.That(text, Does.Not.Contain("value("));
	}

	[Test]
	public void ClosureValueSnapshotNormalizesPrimitiveAndStructuredValues()
	{
		var criteria = new MatcherCriteria("Submit", 2, ["Submit", "Run"], TargetKind.Button, new NestedCriteria("Primary"));
		var lookup = new Dictionary<string, int>(StringComparer.Ordinal)
		{
			["Submit"] = 2,
			["Cancel"] = 1,
		};
		var firstLetter = 'S';
		var cutoff = StableDate;
		var offset = StableDateOffset;
		Expression<Func<MatcherTarget, bool>> expression = target =>
			target.Name == criteria.ExpectedName
			&& criteria.Tags.Contains(target.Name)
			&& lookup.Count >= criteria.MinimumCount
			&& target.Name[0] == firstLetter
			&& target.CreatedUtc >= cutoff
			&& target.CreatedOffset <= offset
			&& target.Kind == criteria.Kind;

		var payload = ExpressionPayloadSerializer.Serialize(expression);

		Assert.That(payload.ClosureValues.Values, Does.Contain("Submit"));
		Assert.That(payload.ClosureValues.Values, Does.Contain(2));
		Assert.That(payload.ClosureValues.Values, Does.Contain("S"));
		Assert.That(payload.ClosureValues.Values, Does.Contain(TargetKind.Button.ToString()));
		Assert.That(payload.ClosureValues.Values, Does.Contain(cutoff.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
		Assert.That(payload.ClosureValues.Values, Does.Contain(offset.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
		Assert.That(payload.ClosureValues.Values.OfType<IReadOnlyList<object?>>().Single(), Is.EqualTo(new object?[] { "Submit", "Run" }));
		Assert.That(payload.ClosureValues.Values.OfType<SortedDictionary<string, object?>>().Single()["Submit"], Is.EqualTo(2));
	}

	[Test]
	public void HashChangesWhenCapturedObjectMemberChanges()
	{
		var criteria = new MutableCriteria { ExpectedName = "Submit" };
		Expression<Func<MatcherTarget, bool>> expression = target => target.Name == criteria.ExpectedName;

		var first = ExpressionPayloadSerializer.Serialize(expression);
		criteria.ExpectedName = "Cancel";
		var second = ExpressionPayloadSerializer.Serialize(expression);

		Assert.That(second.ExpressionHash, Is.Not.EqualTo(first.ExpressionHash));
		Assert.That(second.ClosureValues.Values, Does.Contain("Cancel"));
	}

	[Test]
	public void HashIsStableForEquivalentClosureGraphs()
	{
		var first = ExpressionPayloadSerializer.Serialize(CreateCriteriaExpression("Submit", 2));
		var second = ExpressionPayloadSerializer.Serialize(CreateCriteriaExpression("Submit", 2));

		Assert.That(second.ExpressionHash, Is.EqualTo(first.ExpressionHash));
	}

	[Test]
	public void UnsafeSyncOverAsyncIsStillRejectedInIsolatedExpressionSuite()
	{
		Expression<Func<MatcherTarget, int>> expression = _ => System.Threading.Tasks.Task.FromResult(5).Result;

		var exception = Assert.Throws<InvalidOperationException>(() => ExpressionPayloadSerializer.Serialize(expression));

		Assert.That(exception!.Message, Does.Contain("Task.Result"));
	}

	[TestCaseSource(nameof(UnsupportedCases))]
	public void UnsupportedComplexClosureShapesFailBeforeIpcWithActionableMessage(ExpressionCase testCase)
	{
		var exception = Assert.Throws<InvalidOperationException>(() => ExpressionPayloadSerializer.Serialize(testCase.CreateExpression()));

		Assert.That(exception!.Message, Does.Contain("Expression payload serialization failed"));
		Assert.That(exception.Message, Does.Contain("captured complex object"));
		Assert.That(exception.Message, Does.Contain("IPC boundary"));
		Assert.That(exception.InnerException, Is.Not.Null);
	}

	private static Expression<Func<MatcherTarget, bool>> CreateCriteriaExpression(string expectedName, int minimumCount)
	{
		var criteria = new MatcherCriteria(expectedName, minimumCount, [expectedName], TargetKind.Button, new NestedCriteria("Primary"));
		return target => target.Name == criteria.ExpectedName && target.Count >= criteria.MinimumCount;
	}

	private static IEnumerable<ExpressionCase> RoundTripCases()
	{
		yield return Case(
			"literal string equality",
			() => target => target.Name == "Submit",
			Target(name: "Submit"),
			Target(name: "Cancel"));

		yield return Case(
			"literal numeric comparison",
			() => target => target.Count >= 2,
			Target(count: 3),
			Target(count: 1));

		yield return Case(
			"literal bool negation",
			() => target => !target.IsHidden,
			Target(isHidden: false),
			Target(isHidden: true));

		yield return Case(
			"literal null comparison",
			() => target => target.OptionalName == null,
			Target(optionalName: null),
			Target(optionalName: "Submit"));

		yield return Case(
			"captured string local",
			() =>
			{
				var expectedName = "Submit";
				return target => target.Name == expectedName;
			},
			Target(name: "Submit"),
			Target(name: "Cancel"));

		yield return Case(
			"captured integer local",
			() =>
			{
				var minimumCount = 2;
				return target => target.Count >= minimumCount;
			},
			Target(count: 3),
			Target(count: 1));

		yield return Case(
			"captured nullable local",
			() =>
			{
				int? expected = 5;
				return target => target.OptionalCount == expected;
			},
			Target(optionalCount: 5),
			Target(optionalCount: 4));

		yield return Case(
			"captured null local",
			() =>
			{
				string? expected = null;
				return target => target.OptionalName == expected;
			},
			Target(optionalName: null),
			Target(optionalName: "Submit"));

		yield return Case(
			"captured enum local",
			() =>
			{
				var expectedKind = TargetKind.Button;
				return target => target.Kind == expectedKind;
			},
			Target(kind: TargetKind.Button),
			Target(kind: TargetKind.Label));

		yield return Case(
			"captured char local",
			() =>
			{
				var first = 'S';
				return target => target.Name[0] == first;
			},
			Target(name: "Submit"),
			Target(name: "Cancel"));

		yield return Case(
			"captured array contains",
			() =>
			{
				var names = new[] { "Submit", "Run" };
				return target => names.Contains(target.Name);
			},
			Target(name: "Submit"),
			Target(name: "Cancel"));

		yield return Case(
			"captured record property chain",
			() =>
			{
				var criteria = new MatcherCriteria("Submit", 2, ["Submit"], TargetKind.Button, new NestedCriteria("Primary"));
				return target => target.Name == criteria.ExpectedName && target.Metadata.Category == criteria.Nested.Category;
			},
			Target(name: "Submit", category: "Primary"),
			Target(name: "Submit", category: "Secondary"));

		yield return Case(
			"captured anonymous object property",
			() =>
			{
				var criteria = new { ExpectedName = "Submit", MinimumCount = 2 };
				return target => target.Name == criteria.ExpectedName && target.Count >= criteria.MinimumCount;
			},
			Target(name: "Submit", count: 2),
			Target(name: "Submit", count: 1));

		yield return Case(
			"captured tuple fields",
			() =>
			{
				var criteria = (ExpectedName: "Submit", MinimumCount: 2);
				return target => target.Name == criteria.ExpectedName && target.Count >= criteria.MinimumCount;
			},
			Target(name: "Submit", count: 2),
			Target(name: "Cancel", count: 2));

		yield return Case(
			"captured date time local",
			() =>
			{
				var cutoff = StableDate;
				return target => target.CreatedUtc >= cutoff;
			},
			Target(createdUtc: StableDate.AddMinutes(1)),
			Target(createdUtc: StableDate.AddMinutes(-1)));

		yield return Case(
			"captured date time offset local",
			() =>
			{
				var cutoff = StableDateOffset;
				return target => target.CreatedOffset <= cutoff;
			},
			Target(createdOffset: StableDateOffset.AddMinutes(-1)),
			Target(createdOffset: StableDateOffset.AddMinutes(1)));

		yield return Case(
			"string starts with method",
			() => target => target.Name.StartsWith("Sub", StringComparison.Ordinal),
			Target(name: "Submit"),
			Target(name: "Cancel"));

		yield return Case(
			"string contains method",
			() => target => target.Name.Contains("mit"),
			Target(name: "Submit"),
			Target(name: "Cancel"));

		yield return Case(
			"static string equals overload",
			() =>
			{
				var expected = "submit";
				return target => string.Equals(target.Name, expected, StringComparison.OrdinalIgnoreCase);
			},
			Target(name: "Submit"),
			Target(name: "Cancel"));

		yield return Case(
			"enumerable any method",
			() =>
			{
				var prefix = "Sub";
				return target => target.Tags.Any(tag => tag.StartsWith(prefix, StringComparison.Ordinal));
			},
			Target(tags: ["Submit"]),
			Target(tags: ["Cancel"]));

		yield return Case(
			"logical and composition",
			() => target => target.Name == "Submit" && target.Count >= 2,
			Target(name: "Submit", count: 2),
			Target(name: "Submit", count: 1));

		yield return Case(
			"logical or composition",
			() => target => target.Name == "Submit" || target.Name == "Run",
			Target(name: "Run"),
			Target(name: "Cancel"));

		yield return Case(
			"nested logical composition",
			() => target => (target.Name == "Submit" && target.Count >= 2) || (target.Kind == TargetKind.Button && !target.IsHidden),
			Target(name: "Cancel", kind: TargetKind.Button, isHidden: false),
			Target(name: "Cancel", kind: TargetKind.Button, isHidden: true));

		yield return Case(
			"conditional expression",
			() => target => target.IsHidden ? target.Count == 0 : target.Count >= 2,
			Target(isHidden: false, count: 2),
			Target(isHidden: true, count: 2));

		yield return Case(
			"coalesce expression",
			() => target => (target.OptionalName ?? target.Name) == "Submit",
			Target(name: "Ignored", optionalName: "Submit"),
			Target(name: "Cancel", optionalName: null));

		yield return Case(
			"nested target member access",
			() => target => target.Metadata.Category == "Primary",
			Target(category: "Primary"),
			Target(category: "Secondary"));

		yield return Case(
			"manual null guard for nested member",
			() => target => target.OptionalMetadata != null && target.OptionalMetadata.Category == "Primary",
			Target(optionalCategory: "Primary"),
			Target(optionalCategory: null));

		yield return Case(
			"arithmetic expression",
			() => target => target.Count + target.Offset >= 10,
			Target(count: 7, offset: 3),
			Target(count: 7, offset: 2));
	}

	private static IEnumerable<ExpressionCase> UnsupportedCases()
	{
		yield return Case(
			"captured list contains",
			() =>
			{
				var names = new List<string> { "Submit", "Run" };
				return target => names.Contains(target.Name);
			},
			Target(name: "Run"),
			Target(name: "Cancel"));

		yield return Case(
			"captured dictionary lookup",
			() =>
			{
				var minimumByName = new Dictionary<string, int>(StringComparer.Ordinal)
				{
					["Submit"] = 2,
				};
				return target => minimumByName.ContainsKey(target.Name) && target.Count >= minimumByName[target.Name];
			},
			Target(name: "Submit", count: 2),
			Target(name: "Submit", count: 1));

		yield return Case(
			"captured instance method call",
			() =>
			{
				var criteria = new MutableCriteria { ExpectedName = "Submit" };
				return target => criteria.IsMatch(target.Name);
			},
			Target(name: "Submit"),
			Target(name: "Cancel"));
	}

	private static ExpressionCase Case(
		string name,
		Func<Expression<Func<MatcherTarget, bool>>> createExpression,
		MatcherTarget matchingTarget,
		MatcherTarget nonMatchingTarget) =>
		new(name, createExpression, matchingTarget, nonMatchingTarget);

	private static MatcherTarget Target(
		string name = "Submit",
		string? optionalName = "Submit",
		int count = 2,
		int? optionalCount = 2,
		int offset = 0,
		bool isHidden = false,
		TargetKind kind = TargetKind.Button,
		string category = "Primary",
		string? optionalCategory = "Primary",
		string[]? tags = null,
		DateTime? createdUtc = null,
		DateTimeOffset? createdOffset = null) =>
		new()
		{
			Name = name,
			OptionalName = optionalName,
			Count = count,
			OptionalCount = optionalCount,
			Offset = offset,
			IsHidden = isHidden,
			Kind = kind,
			Metadata = new TargetMetadata { Category = category },
			OptionalMetadata = optionalCategory is null ? null : new TargetMetadata { Category = optionalCategory },
			Tags = tags ?? ["Submit", "Run"],
			CreatedUtc = createdUtc ?? StableDate,
			CreatedOffset = createdOffset ?? StableDateOffset,
		};

	public sealed record ExpressionCase(
		string Name,
		Func<Expression<Func<MatcherTarget, bool>>> CreateExpression,
		MatcherTarget MatchingTarget,
		MatcherTarget NonMatchingTarget)
	{
		public override string ToString() => Name;
	}

	public sealed class MatcherTarget
	{
		public string Name { get; set; } = string.Empty;

		public string? OptionalName { get; set; }

		public int Count { get; set; }

		public int? OptionalCount { get; set; }

		public int Offset { get; set; }

		public bool IsHidden { get; set; }

		public TargetKind Kind { get; set; }

		public TargetMetadata Metadata { get; set; } = new();

		public TargetMetadata? OptionalMetadata { get; set; }

		public string[] Tags { get; set; } = [];

		public DateTime CreatedUtc { get; set; }

		public DateTimeOffset CreatedOffset { get; set; }
	}

	public sealed class TargetMetadata
	{
		public string Category { get; set; } = string.Empty;
	}

	public enum TargetKind
	{
		Unknown,
		Button,
		Label,
	}

	private sealed record MatcherCriteria(
		string ExpectedName,
		int MinimumCount,
		string[] Tags,
		TargetKind Kind,
		NestedCriteria Nested);

	private sealed record NestedCriteria(string Category);

	private sealed class MutableCriteria
	{
		public string ExpectedName { get; set; } = string.Empty;

		public bool IsMatch(string value) =>
			string.Equals(value, ExpectedName, StringComparison.Ordinal);
	}
}
