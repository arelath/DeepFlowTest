namespace DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;

using System.Collections.Generic;
using Forms = System.Windows.Forms;

internal sealed class WinFormsTreeTargetAdapter : TreeTargetAdapterBase
{
	public override bool CanHandle(object target, TargetObjectMetadata metadata) =>
		target is Forms.Control;

	public override IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		var control = (Forms.Control)target;
		foreach (object? child in control.Controls)
			if (child is not null)
				yield return child;

		foreach (var childHwnd in EnumerateNativeChildrenFromMetadata(metadata))
			yield return childHwnd;
	}

	public override bool TryGetIsVisible(object target, out bool isVisible)
	{
		if (target is Forms.Control control)
		{
			isVisible = control.Visible;
			return true;
		}

		return base.TryGetIsVisible(target, out isVisible);
	}
}
