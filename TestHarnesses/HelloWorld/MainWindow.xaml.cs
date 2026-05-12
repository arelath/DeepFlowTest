namespace HelloWorld;

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Threading;
using Drawing = System.Drawing;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinForms = System.Windows.Forms;

public partial class MainWindow : Window
{
	private readonly DispatcherTimer delayedRevealTimer;

	public static Func<Window, MessageBoxResult>? ShowMessageBoxForTests { get; set; }

	public static Func<Window, string?>? ShowOpenFileDialogForTests { get; set; }

	public MainWindow()
	{
		InitializeComponent();
		BuildHostedWinFormsControls();

		delayedRevealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
		delayedRevealTimer.Tick += DelayedRevealTimer_Tick;

		RoutedCommand ctrlA = new();
		ctrlA.InputGestures.Add(new KeyGesture(Key.A, ModifierKeys.Control));
		CommandBindings.Add(new CommandBinding(ctrlA, CtrlA_Shortcut));
	}

	private void HelloWorldButton_Click(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "HelloWorldButton_Click event triggered.";
	}

	private void HelloWorldButton_DoubleClick(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "HelloWorldButton_DoubleClick event triggered.";
	}

	private void HelloWorldButton_RightClick(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "HelloWorldButton_RightClick event triggered.";
	}

	private void HelloWorldButton_ToolTipOpening(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "HelloWorldButton_ToolTipOpening event triggered.";
	}

	private void HelloWorldContextMenuFile_Click(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "HelloWorldContextMenuFile_Click event triggered.";
	}

	private void DelayedReveal_Click(object sender, RoutedEventArgs e)
	{
		DelayedReadyText.Visibility = Visibility.Collapsed;
		delayedRevealTimer.Stop();
		delayedRevealTimer.Start();
	}

	private void DelayedRevealTimer_Tick(object? sender, EventArgs e)
	{
		delayedRevealTimer.Stop();
		DelayedReadyText.Visibility = Visibility.Visible;
		EventDisplay.Text = "DelayedReadyText revealed.";
	}

	private void MainCheckbox_Checked(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "MainCheckbox_Checked event triggered.";
	}

	private void MainCheckbox_Unchecked(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "MainCheckbox_Unchecked event triggered.";
	}

	private void TextBox1_TextChanged(object sender, TextChangedEventArgs e)
	{
		EventDisplay.Text = "TextBox1_TextChanged event triggered.";
	}

	private void TextBox1_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		EventDisplay.Text = "TextBox1_GotKeyboardFocus event triggered.";
	}

	private void TextBox1_TouchDown(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "TextBox1_TouchDown event triggered.";
	}

	private void TextBox1_SelectionChanged(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "TextBox1_SelectionChanged event triggered.";
	}

	private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
	{
		if (sender is ListBox { SelectedItem: ListBoxItem listBoxItem })
			EventDisplay.Text = $"{listBoxItem.Name} selected event triggered.";
	}

	private void ExpanderControl_Expanded(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "ExpanderControl_Expanded event triggered.";
	}

	private void ExpanderControl_Collapsed(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "ExpanderControl_Collapsed event triggered.";
	}

	private void CtrlA_Shortcut(object sender, ExecutedRoutedEventArgs e)
	{
		EventDisplay.Text = "Ctrl+A shortcut triggered.";
	}

	private void OpenOtherWindow_Click(object sender, RoutedEventArgs e)
	{
		var otherWindow = new OtherWindow { Owner = this };
		otherWindow.ShowDialog();
	}

	private void OpenMessageBox_Click(object sender, RoutedEventArgs e)
	{
		var result = ShowMessageBoxForTests?.Invoke(this)
			?? MessageBox.Show(this, "Sample Message Box Text", "A caption.", MessageBoxButton.YesNo);
		if (result == MessageBoxResult.Yes)
			EventDisplay.Text = "Chose Yes.";
		else if (result == MessageBoxResult.No)
			EventDisplay.Text = "Chose No.";
	}

	private void OpenFileDialog_Click(object sender, RoutedEventArgs e)
	{
		if (ShowOpenFileDialogForTests is { } testDialog)
		{
			var selectedFile = testDialog(this);
			EventDisplay.Text = string.IsNullOrWhiteSpace(selectedFile)
				? "Open file dialog canceled."
				: $"Opened file: {Path.GetFileName(selectedFile)}";
			return;
		}

		var dialog = new OpenFileDialog
		{
			Title = "DeepFlowTest Open File",
			CheckFileExists = true,
			Multiselect = false,
		};

		var result = dialog.ShowDialog(this);
		EventDisplay.Text = result == true
			? $"Opened file: {Path.GetFileName(dialog.FileName)}"
			: "Open file dialog canceled.";
	}

	private void ThrowException_Click(object sender, RoutedEventArgs e)
	{
		throw new InvalidOperationException("Oh no an unhandled exception!");
	}

	private void BuildHostedWinFormsControls()
	{
		var panel = new WinForms.Panel
		{
			Name = "HostedWinFormsPanel",
			Size = new Drawing.Size(220, 132),
		};

		var textBox = new WinForms.TextBox
		{
			Name = "HostedWinFormsTextBox",
			Location = new Drawing.Point(8, 8),
			Width = 190,
		};
		textBox.TextChanged += (_, _) =>
		{
			EventDisplay.Text = $"HostedWinFormsTextBox_TextChanged: {textBox.Text}";
		};

		var checkBox = new WinForms.CheckBox
		{
			Name = "HostedWinFormsCheckBox",
			Text = "Hosted CheckBox",
			Location = new Drawing.Point(8, 42),
			AutoSize = true,
		};
		checkBox.CheckedChanged += (_, _) =>
		{
			EventDisplay.Text = checkBox.Checked
				? "HostedWinFormsCheckBox_CheckedChanged: checked"
				: "HostedWinFormsCheckBox_CheckedChanged: unchecked";
		};

		var button = new WinForms.Button
		{
			Name = "HostedWinFormsButton",
			Text = "Hosted Button",
			Location = new Drawing.Point(8, 78),
			AutoSize = true,
		};
		button.Click += (_, _) =>
		{
			EventDisplay.Text = "HostedWinFormsButton_Click event triggered.";
		};

		panel.Controls.Add(textBox);
		panel.Controls.Add(checkBox);
		panel.Controls.Add(button);

		var host = new WindowsFormsHost
		{
			Name = "WinFormsHostIsland",
			Width = 230,
			Height = 142,
			Child = panel,
		};

		HostedWinFormsContainer.Children.Add(host);
	}
}
