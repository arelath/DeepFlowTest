namespace HelloWorld;

using System;
using System.IO;
using System.Reflection;
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
	private Point? dragStartPoint;

	public static Func<Window, MessageBoxResult>? ShowMessageBoxForTests { get; set; }

	public static Func<Window, string?>? ShowOpenFileDialogForTests { get; set; }

	// Data-bound children for the dynamic submenus (mirrors Sage's BuildFolderMenuItems / KeyFilters
	// ItemsSource). These are custom objects whose Header text comes from a child property surfaced
	// through an ItemContainerStyle, exactly like Sage's menus — not plain strings — so the harness
	// reproduces Sage's nested dynamic-submenu realization behavior faithfully.
	public System.Collections.Generic.IReadOnlyList<MenuEntry> NestedDynamicItems { get; } =
		new[]
		{
			new MenuEntry("NestedDynamicChildA"),
			new MenuEntry("NestedDynamicChildB"),
			new MenuEntry("NestedDynamicChildC"),
		};

	// Mirror of a Sage menu data item (e.g. BuildFolderMenuItem): the visible label is exposed via a
	// child property that the MenuItem.ItemContainerStyle binds to MenuItem.Header.
	public sealed class MenuEntry
	{
		public MenuEntry(string label) => Label = label;

		public string Label { get; }
	}

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

	private void HelloWorldButton_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Middle)
			EventDisplay.Text = "HelloWorldButton_MiddleClick event triggered.";
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

	private void MenuItemOne_Click(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "MenuItemOne_Click event triggered.";
	}

	private void MenuItemTwo_Click(object sender, RoutedEventArgs e)
	{
		EventDisplay.Text = "MenuItemTwo_Click event triggered.";
	}

	private void CtrlA_Shortcut(object sender, ExecutedRoutedEventArgs e)
	{
		EventDisplay.Text = "Ctrl+A shortcut triggered.";
	}

	private void DragDropSource_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		dragStartPoint = e.GetPosition(null);
		AppendDragDropEvent("DragDropSource:MouseDown");
	}

	private void DragDropSource_MouseMove(object sender, MouseEventArgs e)
	{
		if (sender is not DependencyObject source || e.LeftButton != MouseButtonState.Pressed || dragStartPoint is not { } start)
			return;

		var current = e.GetPosition(null);
		if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
			&& Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}

		dragStartPoint = null;
		AppendDragDropEvent("DragDropSource:DragStart");
		DragDrop.DoDragDrop(source, "DeepFlowTestDragPayload", DragDropEffects.Move);
	}

	private void DragDropZone_MouseEnter(object sender, MouseEventArgs e)
	{
		AppendDragDropEvent($"{DragDropZoneName(sender)}:MouseEnter");
	}

	private void DragDropZone_MouseLeave(object sender, MouseEventArgs e)
	{
		AppendDragDropEvent($"{DragDropZoneName(sender)}:MouseLeave");
	}

	private void DragDropZone_DragEnter(object sender, DragEventArgs e)
	{
		e.Effects = IsHarnessDragPayload(e) ? DragDropEffects.Move : DragDropEffects.None;
		AppendDragDropEvent($"{DragDropZoneName(sender)}:DragEnter");
		e.Handled = true;
	}

	private void DragDropZone_DragLeave(object sender, DragEventArgs e)
	{
		AppendDragDropEvent($"{DragDropZoneName(sender)}:DragLeave");
		e.Handled = true;
	}

	private void DragDropZone_Drop(object sender, DragEventArgs e)
	{
		var zoneName = DragDropZoneName(sender);
		AppendDragDropEvent($"{zoneName}:Drop");
		EventDisplay.Text = $"{zoneName}_Drop event triggered.";
		if (sender is Border { Child: TextBlock label })
			label.Text = $"{zoneName} received drop";

		e.Effects = IsHarnessDragPayload(e) ? DragDropEffects.Move : DragDropEffects.None;
		e.Handled = true;
	}

	public void RunInjectedDragDropProbe()
	{
		DragDropEventLog.Text = string.Empty;
		AppendDragDropEvent("DragDropSource:InjectedStart");

		RaiseMouseEvent(DragDropTransitTarget, Mouse.MouseEnterEvent);
		RaiseDragEvent(DragDropTransitTarget, DragDrop.DragEnterEvent);
		RaiseMouseEvent(DragDropTransitTarget, Mouse.MouseLeaveEvent);
		RaiseDragEvent(DragDropTransitTarget, DragDrop.DragLeaveEvent);

		RaiseMouseEvent(DragDropFinalTarget, Mouse.MouseEnterEvent);
		RaiseDragEvent(DragDropFinalTarget, DragDrop.DragEnterEvent);
		RaiseDragEvent(DragDropFinalTarget, DragDrop.DropEvent);
		RaiseMouseEvent(DragDropFinalTarget, Mouse.MouseLeaveEvent);
	}

	private static bool IsHarnessDragPayload(DragEventArgs e) =>
		e.Data.GetDataPresent(DataFormats.StringFormat)
		&& string.Equals(e.Data.GetData(DataFormats.StringFormat) as string, "DeepFlowTestDragPayload", StringComparison.Ordinal);

	private static void RaiseMouseEvent(UIElement target, RoutedEvent routedEvent)
	{
		target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
		{
			RoutedEvent = routedEvent,
			Source = target,
		});
	}

	private static void RaiseDragEvent(UIElement target, RoutedEvent routedEvent)
	{
		var data = new DataObject(DataFormats.StringFormat, "DeepFlowTestDragPayload");
		var args = CreateDragEventArgs(data, target);
		args.RoutedEvent = routedEvent;
		args.Source = target;
		target.RaiseEvent(args);
	}

	private static DragEventArgs CreateDragEventArgs(IDataObject data, DependencyObject target)
	{
		var constructor = typeof(DragEventArgs).GetConstructor(
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
			binder: null,
			types: new[]
			{
				typeof(IDataObject),
				typeof(DragDropKeyStates),
				typeof(DragDropEffects),
				typeof(DependencyObject),
				typeof(Point),
			},
			modifiers: null) ?? throw new InvalidOperationException("Could not find the WPF DragEventArgs constructor.");

		return (DragEventArgs)constructor.Invoke(new object[]
		{
			data,
			DragDropKeyStates.LeftMouseButton,
			DragDropEffects.Move,
			target,
			new Point(1, 1),
		});
	}

	private static string DragDropZoneName(object sender) =>
		sender is FrameworkElement { Name.Length: > 0 } element ? element.Name : "UnknownDragDropZone";

	private void AppendDragDropEvent(string eventName)
	{
		DragDropEventLog.Text = string.IsNullOrWhiteSpace(DragDropEventLog.Text)
			? eventName
			: $"{DragDropEventLog.Text}|{eventName}";
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
