namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System.Collections;
using System.Collections.Generic;
using System.Windows;

internal sealed class ResourceDictionaryTreeTargetAdapter : TreeTargetAdapterBase
{
	public override bool CanHandle(object target, TargetObjectMetadata metadata) =>
		target is ResourceDictionary;

	public override IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		var resourceDictionary = (ResourceDictionary)target;
		foreach (var mergedDictionary in resourceDictionary.MergedDictionaries)
			yield return mergedDictionary;

		foreach (var item in resourceDictionary)
			if (item is DictionaryEntry entry && entry.Value is not null)
				yield return entry.Value;
	}
}
