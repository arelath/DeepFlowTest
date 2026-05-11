namespace HelloWorld;

using System.Windows;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
	}

	private void ActionButton_Click(object sender, RoutedEventArgs e)
	{
		InputTextBox.Text = "Button clicked";
	}
}
