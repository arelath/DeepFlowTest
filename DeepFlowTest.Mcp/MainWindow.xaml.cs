namespace DeepFlowTest.Mcp;

using System.Windows;
using DeepFlowTest.Mcp.ViewModels;

internal partial class MainWindow : Window
{
	public MainWindow(MainWindowViewModel viewModel)
	{
		InitializeComponent();
		DataContext = viewModel;
	}
}
