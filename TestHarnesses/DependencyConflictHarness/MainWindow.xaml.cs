namespace DependencyConflictHarness;

using System.Windows;
using Newtonsoft.Json;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
		TargetNewtonsoftVersion.Text = typeof(JsonConvert).Assembly.GetName().Version?.ToString() ?? "unknown";
	}

	private void TargetSerializeButton_OnClick(object sender, RoutedEventArgs e)
	{
		TargetSerializationResult.Text = JsonConvert.SerializeObject(new { message = "target-ok" });
	}
}
