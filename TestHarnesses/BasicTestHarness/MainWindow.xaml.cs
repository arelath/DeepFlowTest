namespace BasicTestHarness;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
	}

	private void PopupToggle_Click(object sender, RoutedEventArgs e)
	{
		SamplePopup.IsOpen = true;
	}

	private void ShowSecondaryWindow_Click(object sender, RoutedEventArgs e)
	{
		var content = new TextBlock
		{
			Margin = new Thickness(16),
			Text = "Secondary window content",
		};
		AutomationProperties.SetAutomationId(content, "SecondaryWindowText");
		AutomationProperties.SetName(content, "Secondary Window Text");

		var window = new Window
		{
			Owner = this,
			Title = "DeepFlowTest Secondary Window",
			Name = "SecondaryHarnessWindow",
			Width = 320,
			Height = 180,
			Content = content,
		};
		AutomationProperties.SetAutomationId(window, "SecondaryHarnessWindow");
		AutomationProperties.SetName(window, "Secondary Harness Window");
		window.Show();
	}

	private void ShowModalDialog_Click(object sender, RoutedEventArgs e)
	{
		MessageBox.Show(this, "Modal dialog content", "DeepFlowTest Modal Dialog", MessageBoxButton.OK, MessageBoxImage.Information);
	}
}
