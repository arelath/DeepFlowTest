namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System.Collections.Generic;
using System.Windows;

internal sealed class ApplicationTreeTargetAdapter : TreeTargetAdapterBase
{
	public override bool CanHandle(object target, TargetObjectMetadata metadata) =>
		target is Application;

	public override IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		var application = (Application)target;
		if (application.Resources.Count != 0)
			yield return application.Resources;

		foreach (Window? window in application.Windows)
			yield return window;
	}
}
