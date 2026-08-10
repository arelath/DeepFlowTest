namespace DeepFlowTest.Utility.WpfUtility.Tree.Wrappers;

using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

internal sealed class WpfTargetObjectWrapper : TargetObjectWrapper
{
	public WpfTargetObjectWrapper(DependencyObject target)
		: base(target, CreateWpfMetadata(target))
	{
	}

	private static TargetObjectMetadata CreateWpfMetadata(DependencyObject target)
	{
		var kind = target switch
		{
			Visual => TargetObjectKind.WpfVisual,
			Visual3D => TargetObjectKind.WpfVisual,
			ContentElement => TargetObjectKind.WpfLogicalObject,
			_ => TargetObjectKind.WpfDependencyObject,
		};

		return CreateMetadata(
			target,
			kind,
			"wpf",
			target is UIElement or ContentElement,
			TryGetWpfHwnd(target));
	}

	private static long? TryGetWpfHwnd(DependencyObject target)
	{
		if (target is Window window)
		{
			var windowHandle = new WindowInteropHelper(window).Handle;
			if (windowHandle != IntPtr.Zero)
				return windowHandle.ToInt64();
		}

		if (target is Visual visual && PresentationSource.FromVisual(visual) is HwndSource source && source.Handle != IntPtr.Zero)
			return source.Handle.ToInt64();

		return null;
	}
}
