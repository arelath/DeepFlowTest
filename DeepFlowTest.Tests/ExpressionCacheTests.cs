namespace DeepFlowTest.Tests;

using System;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Interop;
using NUnit.Framework;

[TestFixture]
public sealed class ExpressionCacheTests
{
	[Test]
	public void RepeatedPollingCompilesOnceAndReportsHit()
	{
		var cache = new ExpressionCache(capacity: 8);
		var compileCount = 0;

		var first = cache.GetOrCompile("hash-a", () =>
		{
			compileCount++;
			return (Func<int, bool>)(value => value > 0);
		});
		var second = cache.GetOrCompile("hash-a", () =>
		{
			compileCount++;
			return (Func<int, bool>)(_ => false);
		});

		Assert.That(second, Is.SameAs(first));
		Assert.That(second(1), Is.True);
		Assert.That(compileCount, Is.EqualTo(1));
		Assert.That(cache.Stats.HitCount, Is.EqualTo(1));
		Assert.That(cache.Stats.MissCount, Is.EqualTo(1));
		Assert.That(cache.Stats.CompileCount, Is.EqualTo(1));
		Assert.That(cache.Stats.CurrentEntryCount, Is.EqualTo(1));
	}

	[Test]
	public void BoundedCacheEvictsLeastRecentlyUsedEntry()
	{
		var cache = new ExpressionCache(capacity: 2);
		var compileCount = 0;

		cache.GetOrCompile("a", () => Compile(ref compileCount, "a"));
		cache.GetOrCompile("b", () => Compile(ref compileCount, "b"));
		cache.GetOrCompile("a", () => Compile(ref compileCount, "a2"));
		cache.GetOrCompile("c", () => Compile(ref compileCount, "c"));

		Assert.That(cache.Contains("a"), Is.True);
		Assert.That(cache.Contains("b"), Is.False);
		Assert.That(cache.Contains("c"), Is.True);
		Assert.That(cache.Stats.EvictionCount, Is.EqualTo(1));
		Assert.That(cache.Stats.CurrentEntryCount, Is.EqualTo(2));
		Assert.That(compileCount, Is.EqualTo(3));
	}

	[Test]
	public void FailedCompileDoesNotPoisonFutureCacheEntry()
	{
		var cache = new ExpressionCache(capacity: 2);
		var compileCount = 0;

		Assert.That(
			() => cache.GetOrCompile<Func<int, bool>>("bad-then-good", () =>
			{
				compileCount++;
				throw new InvalidOperationException("bad expression");
			}),
			Throws.TypeOf<InvalidOperationException>());

		Assert.That(cache.Contains("bad-then-good"), Is.False);

		var compiled = cache.GetOrCompile("bad-then-good", () =>
		{
			compileCount++;
			return (Func<int, bool>)(value => value == 42);
		});

		Assert.That(compiled(42), Is.True);
		Assert.That(compileCount, Is.EqualTo(2));
		Assert.That(cache.Stats.CurrentEntryCount, Is.EqualTo(1));
	}

	[Test]
	public void PayloadOverloadUsesExpressionHash()
	{
		var cache = new ExpressionCache();
		var payload = new ExpressionMatcherPayload { ExpressionHash = "payload-hash" };

		cache.GetOrCompile(payload, _ => (Func<int, bool>)(value => value < 10));

		Assert.That(cache.Contains("payload-hash"), Is.True);
	}

	private static Func<int, bool> Compile(ref int compileCount, string marker)
	{
		compileCount++;
		return value => marker.Length + value > 0;
	}
}
