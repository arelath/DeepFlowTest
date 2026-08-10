namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System;
using System.Collections.Generic;
using static DeepFlowTest.Utility.WpfUtility.Tree.TreeService;

internal abstract class TreeTargetAdapterBase : ITreeTargetAdapter
{
	public virtual bool StopsChildEnumeration => false;

	public abstract bool CanHandle(object target, TargetObjectMetadata metadata);

	public virtual IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		yield break;
	}

	public virtual bool TryGetIsVisible(object target, out bool isVisible)
	{
		isVisible = true;
		return false;
	}

	protected static IEnumerable<IntPtr> EnumerateNativeChildrenFromMetadata(TargetObjectMetadata metadata)
	{
		if (!metadata.Hwnd.HasValue)
			yield break;

		var hwnd = new IntPtr(metadata.Hwnd.Value);
		foreach (var childHwnd in EnumerateNativeChildWindows(hwnd))
			yield return childHwnd;
	}
}
