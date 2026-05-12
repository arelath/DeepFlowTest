namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Interop;

public sealed class ExpressionCache
{
	public const int DefaultCapacity = 64;

	private readonly object gate = new();
	private readonly int capacity;
	private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);
	private long accessCounter;
	private long hitCount;
	private long missCount;
	private long compileCount;
	private long evictionCount;

	public ExpressionCache(int capacity = DefaultCapacity)
	{
		if (capacity < 1)
			throw new ArgumentOutOfRangeException(nameof(capacity), "Expression cache capacity must be at least one.");

		this.capacity = capacity;
	}

	public ExpressionCacheStats Stats
	{
		get
		{
			lock (gate)
			{
				return new ExpressionCacheStats
				{
					Capacity = capacity,
					CurrentEntryCount = entries.Count,
					HitCount = hitCount,
					MissCount = missCount,
					CompileCount = compileCount,
					EvictionCount = evictionCount,
				};
			}
		}
	}

	public TDelegate GetOrCompile<TDelegate>(ExpressionMatcherPayload payload, Func<ExpressionMatcherPayload, TDelegate> compile)
		where TDelegate : Delegate
	{
		_ = payload ?? throw new ArgumentNullException(nameof(payload));
		_ = compile ?? throw new ArgumentNullException(nameof(compile));

		return GetOrCompile(payload.ExpressionHash, () => compile(payload));
	}

	public TDelegate GetOrCompile<TDelegate>(string expressionHash, Func<TDelegate> compile)
		where TDelegate : Delegate
	{
		if (string.IsNullOrWhiteSpace(expressionHash))
			throw new ArgumentException("Expression hash is required.", nameof(expressionHash));
		_ = compile ?? throw new ArgumentNullException(nameof(compile));

		lock (gate)
		{
			if (entries.TryGetValue(expressionHash, out var entry))
			{
				if (entry.CompiledDelegate is TDelegate cached)
				{
					hitCount++;
					entry.LastAccess = NextAccess();
					return cached;
				}

				entries.Remove(expressionHash);
			}

			missCount++;
			compileCount++;
			try
			{
				var compiled = compile();
				if (compiled is null)
					throw new InvalidOperationException("Expression compiler returned null.");

				entries[expressionHash] = new CacheEntry(compiled, NextAccess());
				EvictIfNeeded();
				return compiled;
			}
			catch
			{
				entries.Remove(expressionHash);
				throw;
			}
		}
	}

	public bool Contains(string expressionHash)
	{
		lock (gate)
		{
			return entries.ContainsKey(expressionHash);
		}
	}

	public void Clear()
	{
		lock (gate)
		{
			entries.Clear();
		}
	}

	private long NextAccess() => ++accessCounter;

	private void EvictIfNeeded()
	{
		while (entries.Count > capacity)
		{
			var oldest = entries.OrderBy(static item => item.Value.LastAccess).First();
			entries.Remove(oldest.Key);
			evictionCount++;
		}
	}

	private sealed class CacheEntry
	{
		public CacheEntry(Delegate compiledDelegate, long lastAccess)
		{
			CompiledDelegate = compiledDelegate;
			LastAccess = lastAccess;
		}

		public Delegate CompiledDelegate { get; }

		public long LastAccess { get; set; }
	}
}

public sealed class ExpressionCacheStats
{
	public int Capacity { get; set; }

	public int CurrentEntryCount { get; set; }

	public long HitCount { get; set; }

	public long MissCount { get; set; }

	public long CompileCount { get; set; }

	public long EvictionCount { get; set; }
}
