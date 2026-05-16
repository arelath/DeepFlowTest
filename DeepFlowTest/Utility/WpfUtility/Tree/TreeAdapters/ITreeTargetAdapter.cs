namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System.Collections.Generic;

internal interface ITreeTargetAdapter
{
	bool StopsChildEnumeration { get; }

	bool CanHandle(object target, TargetObjectMetadata metadata);

	IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata);

	bool TryGetIsVisible(object target, out bool isVisible);
}
