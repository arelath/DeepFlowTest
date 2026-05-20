namespace DeepFlowTest.Recorder;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

public partial class MainWindow : Window
{
	private readonly ObservableCollection<ProcessRow> visibleProcesses = [];
	private IReadOnlyList<ProcessRow> allProcesses = [];
	private AppDriver? driver;
	private SemanticRecordingSession? recording;
	private bool isBusy;

	public MainWindow()
	{
		InitializeComponent();
		ProcessGrid.ItemsSource = visibleProcesses;
		OutputPathTextBox.Text = CreateDefaultOutputPath();
	}

	private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshProcessesAsync();

	private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshProcessesAsync();

	private void FilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

	private void BrowseOutput_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new SaveFileDialog
		{
			Title = "Recording output",
			Filter = "JSON Lines (*.jsonl)|*.jsonl|All files (*.*)|*.*",
			FileName = Path.GetFileName(OutputPathTextBox.Text),
			InitialDirectory = ResolveInitialOutputDirectory(),
			OverwritePrompt = true,
		};

		if (dialog.ShowDialog(this) == true)
			OutputPathTextBox.Text = dialog.FileName;
	}

	private async void StartRecording_Click(object sender, RoutedEventArgs e)
	{
		if (ProcessGrid.SelectedItem is not ProcessRow selected)
		{
			SetStatus("Select a process first.");
			return;
		}

		var outputPath = OutputPathTextBox.Text.Trim();
		if (string.IsNullOrWhiteSpace(outputPath))
		{
			SetStatus("Choose an output file.");
			return;
		}

		await RunBusyAsync(async () =>
		{
			SetStatus($"Starting recording for {selected.DisplayName}...");
			var started = await Task.Run(() =>
			{
				AppDriver? attachedDriver = null;
				var attachOptions = new AppDriverAttachOptions
				{
					Timeout = TimeSpan.FromSeconds(30),
					PayloadRoot = AppContext.BaseDirectory,
				};
				try
				{
					attachedDriver = AppDriver.AttachTo(selected.ProcessId, attachOptions);
					var session = attachedDriver.StartSemanticRecording(outputPath, new SemanticRecordingOptions
					{
						IntervalMs = 250,
						TimeoutMs = 30_000,
					});
					return (Driver: attachedDriver, Recording: session);
				}
				catch
				{
					attachedDriver?.Dispose();
					throw;
				}
			});

			driver = started.Driver;
			recording = started.Recording;
			SetRecordingState(true);
			SetStatus($"Recording {selected.DisplayName} to {recording.OutputPath}");
		});
	}

	private async void StopRecording_Click(object sender, RoutedEventArgs e) => await StopRecordingAsync();

	private async void Window_Closing(object? sender, CancelEventArgs e)
	{
		if (recording is null)
			return;

		e.Cancel = true;
		await StopRecordingAsync();
		Close();
	}

	private async Task RefreshProcessesAsync()
	{
		if (recording is not null)
			return;

		await RunBusyAsync(async () =>
		{
			SetStatus("Refreshing processes...");
			allProcesses = await Task.Run(LoadProcessRows);
			ApplyFilter();
			SetStatus(allProcesses.Count == 0 ? "No windowed processes found." : $"Found {allProcesses.Count} processes.");
		});
	}

	private async Task StopRecordingAsync()
	{
		var session = recording;
		var attachedDriver = driver;
		if (session is null)
			return;

		recording = null;
		driver = null;
		await RunBusyAsync(async () =>
		{
			SetStatus("Stopping recording...");
			await Task.Run(() =>
			{
				try
				{
					session.Dispose();
				}
				finally
				{
					attachedDriver?.Dispose();
				}
			});
			SetRecordingState(false);
			SetStatus($"Stopped. Frames: {session.FramesWritten}; dropped actions: {session.DroppedActionCount}.");
		});
	}

	private async Task RunBusyAsync(Func<Task> action)
	{
		if (isBusy)
			return;

		isBusy = true;
		UpdateControls();
		try
		{
			await action();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			var failure = RecorderFailureFormatter.Format(ex);
			SetStatus(failure.Status, failure.Details);
		}
		finally
		{
			isBusy = false;
			UpdateControls();
		}
	}

	private void ApplyFilter()
	{
		var filter = FilterTextBox.Text.Trim();
		var rows = string.IsNullOrWhiteSpace(filter)
			? allProcesses
			: allProcesses
				.Where(row => row.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
					|| row.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(filter, StringComparison.OrdinalIgnoreCase)
					|| row.WindowTitle.Contains(filter, StringComparison.OrdinalIgnoreCase))
				.ToArray();

		visibleProcesses.Clear();
		foreach (var row in rows)
			visibleProcesses.Add(row);

		if (visibleProcesses.Count > 0 && ProcessGrid.SelectedItem is null)
			ProcessGrid.SelectedIndex = 0;
	}

	private void SetRecordingState(bool isRecording)
	{
		ProcessGrid.IsEnabled = !isRecording;
		FilterTextBox.IsEnabled = !isRecording;
		RefreshButton.IsEnabled = !isRecording;
		OutputPathTextBox.IsEnabled = !isRecording;
		StartButton.IsEnabled = !isRecording;
		StopButton.IsEnabled = isRecording;
	}

	private void UpdateControls()
	{
		var isRecording = recording is not null;
		ProcessGrid.IsEnabled = !isBusy && !isRecording;
		FilterTextBox.IsEnabled = !isBusy && !isRecording;
		RefreshButton.IsEnabled = !isBusy && !isRecording;
		OutputPathTextBox.IsEnabled = !isBusy && !isRecording;
		StartButton.IsEnabled = !isBusy && !isRecording;
		StopButton.IsEnabled = !isBusy && isRecording;
	}

	private void SetStatus(string status) => SetStatus(status, details: null);

	private void SetStatus(string status, string? details)
	{
		StatusTextBlock.Text = status;
		if (string.IsNullOrWhiteSpace(details))
		{
			FailureDetailsTextBox.Text = string.Empty;
			FailureDetailsExpander.Visibility = Visibility.Collapsed;
			FailureDetailsExpander.IsExpanded = false;
			return;
		}

		FailureDetailsTextBox.Text = details;
		FailureDetailsExpander.Visibility = Visibility.Visible;
		FailureDetailsExpander.IsExpanded = true;
	}

	private void CopyFailureDetails_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(FailureDetailsTextBox.Text))
			Clipboard.SetText(FailureDetailsTextBox.Text);
	}

	private string ResolveInitialOutputDirectory()
	{
		var path = OutputPathTextBox.Text.Trim();
		var directory = string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
		return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
			? directory
			: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
	}

	private static IReadOnlyList<ProcessRow> LoadProcessRows()
	{
		var currentProcessId = Environment.ProcessId;
		var rows = new List<ProcessRow>();
		foreach (var process in Process.GetProcesses())
		{
			using (process)
			{
				try
				{
					if (process.Id == currentProcessId || process.HasExited || process.MainWindowHandle == IntPtr.Zero)
						continue;

					var title = process.MainWindowTitle;
					if (string.IsNullOrWhiteSpace(title))
						continue;

					rows.Add(new ProcessRow(process.Id, process.ProcessName, title));
				}
				catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
				{
				}
			}
		}

		return rows
			.OrderBy(static row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static row => row.ProcessId)
			.ToArray();
	}

	private static string CreateDefaultOutputPath()
	{
		var directory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
			"DeepFlowTestRecordings");
		Directory.CreateDirectory(directory);
		return Path.Combine(directory, $"recording-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
	}

	public sealed record ProcessRow(int ProcessId, string DisplayName, string WindowTitle);
}
