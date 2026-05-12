namespace DeepFlowTest.Utility.WpfUtility.Tree;

using System.Windows;

internal sealed class SystemResourceRoot
{
	public ResourceDictionary Resources { get; } = SystemResources();

	private static ResourceDictionary SystemResources()
	{
		var resources = new ResourceDictionary();
		resources["SystemColors.ControlBrushKey"] = SystemColors.ControlBrushKey;
		resources["SystemFonts.MessageFontSize"] = SystemFonts.MessageFontSize;
		resources["SystemParameters.WorkArea"] = SystemParameters.WorkArea;
		return resources;
	}
}
