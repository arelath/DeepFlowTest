namespace DeepFlowTest.Utility;

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

public sealed class TargetIdService
{
	private readonly ConditionalWeakTable<object, TargetIdHolder> idsByTarget = new();
	private readonly ConcurrentDictionary<string, WeakReference<object>> targetsById = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, object> valueTargetsById = new(StringComparer.Ordinal);
	private long nextId;

	public string GetOrCreateId(object target)
	{
		_ = target ?? throw new ArgumentNullException(nameof(target));

		if (target is IntPtr hwnd)
		{
			var id = $"dft-hwnd-{hwnd.ToInt64():x}";
			valueTargetsById[id] = hwnd;
			return id;
		}

		var holder = idsByTarget.GetValue(target, _ =>
		{
			var id = $"dft-target-{Interlocked.Increment(ref nextId):x}";
			targetsById[id] = new WeakReference<object>(target);
			return new TargetIdHolder(id);
		});
		return holder.TargetId;
	}

	public TargetIdResolution Resolve(string targetId)
	{
		if (string.IsNullOrWhiteSpace(targetId))
			return TargetIdResolution.NotFound(targetId);

		if (!targetsById.TryGetValue(targetId, out var weakReference))
		{
			if (valueTargetsById.TryGetValue(targetId, out var valueTarget))
			{
				if (valueTarget is IntPtr hwnd && !IsWindow(hwnd))
				{
					valueTargetsById.TryRemove(targetId, out _);
					return TargetIdResolution.Stale(targetId);
				}

				return TargetIdResolution.Found(targetId, valueTarget);
			}

			return TargetIdResolution.NotFound(targetId);
		}

		if (weakReference.TryGetTarget(out var target))
			return TargetIdResolution.Found(targetId, target);

		return TargetIdResolution.Stale(targetId);
	}

	public bool TryGetTarget(string targetId, out object? target)
	{
		var resolution = Resolve(targetId);
		target = resolution.Target;
		return resolution.Status == TargetIdResolutionStatus.Found;
	}

	private sealed class TargetIdHolder
	{
		public TargetIdHolder(string targetId)
		{
			TargetId = targetId;
		}

		public string TargetId { get; }
	}

	[DllImport("user32.dll")]
	private static extern bool IsWindow(IntPtr hWnd);
}

public sealed class TargetIdResolution
{
	private TargetIdResolution(string targetId, TargetIdResolutionStatus status, object? target)
	{
		TargetId = targetId;
		Status = status;
		Target = target;
	}

	public string TargetId { get; }

	public TargetIdResolutionStatus Status { get; }

	public object? Target { get; }

	public static TargetIdResolution Found(string targetId, object target) =>
		new(targetId, TargetIdResolutionStatus.Found, target);

	public static TargetIdResolution Stale(string targetId) =>
		new(targetId, TargetIdResolutionStatus.Stale, null);

	public static TargetIdResolution NotFound(string targetId) =>
		new(targetId, TargetIdResolutionStatus.NotFound, null);
}

public enum TargetIdResolutionStatus
{
	Found,
	Stale,
	NotFound,
}
