namespace DeepFlowTest.Tests;

using System.Windows;

internal static class WpfTestHelpers
{
	public static Window CreateWindow(string title, object content, double width = 260, double height = 180)
	{
		return new Window
		{
			Title = title,
			Content = content,
			Width = width,
			Height = height,
			ShowInTaskbar = false,
			WindowStartupLocation = WindowStartupLocation.Manual,
			Left = -20000,
			Top = -20000,
		};
	}
}
