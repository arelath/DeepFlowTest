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
using System.Windows.Controls;
using DeepFlowTest.Contracts;
using Microsoft.Win32;

public partial class MainWindow : Window
{
	private readonly ObservableCollection<ProcessRow> visibleProcesses = [];
	private readonly RecordingSessionViewModel reviewModel = new();
	private IReadOnlyList<ProcessRow> allProcesses = [];
	private AppDriver? driver;
	private SemanticRecordingSession? recording;
	private bool isBusy;
	private bool isReviewMode;

	public MainWindow()
	{
		InitializeComponent();
		ProcessGrid.ItemsSource = visibleProcesses;
		ReviewGrid.DataContext = reviewModel;
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
			Filter = "DeepFlowTest condensed recording (*.dft.txt)|*.dft.txt|JSON recording (*.json)|*.json|All files (*.*)|*.*",
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

		var startedRecording = false;
		await RunBusyAsync(async () =>
		{
			SetStatus($"Starting recording for {selected.DisplayName}...");
			reviewModel.Reset();
			SetReviewMode(true);
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
						Interval = TimeSpan.FromMilliseconds(250),
						Timeout = TimeSpan.FromSeconds(30),
						OutputFormat = SemanticRecordingOutputFormat.CondensedAgent,
						BatchReceived = batch => Dispatcher.BeginInvoke(new Action(() => ReceiveRecordingBatch(batch))),
						BatchReceivedError = ex => Dispatcher.BeginInvoke(new Action(() => ShowVisualizerFailure(ex))),
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
			startedRecording = true;
			SetRecordingState(true);
			SetStatus($"Recording {selected.DisplayName} to {recording.OutputPath}");
		});

		if (!startedRecording && recording is null)
			SetReviewMode(false);
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
			SetReviewMode(false);
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

	private void ReceiveRecordingBatch(SemanticRecordingBatch batch)
	{
		reviewModel.ReceiveBatch(batch);
		if (reviewModel.SelectedFrame is not null)
			FrameListBox.ScrollIntoView(reviewModel.SelectedFrame);

		if (!string.IsNullOrWhiteSpace(reviewModel.ProjectionErrorDetails))
		{
			SetStatus("Tree visualizer hit a projection error; recording continues.", reviewModel.ProjectionErrorDetails);
			return;
		}

		if (recording is not null)
			SetStatus($"Recording. Captured frames: {reviewModel.Frames.Count}.");
	}

	private void ShowVisualizerFailure(Exception ex)
	{
		var failure = RecorderFailureFormatter.Format(ex);
		SetStatus("Tree visualizer callback failed; recording continues.", failure.Details);
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
		ProcessGrid.IsEnabled = !isRecording && !isReviewMode;
		FilterTextBox.IsEnabled = !isRecording && !isReviewMode;
		RefreshButton.IsEnabled = !isRecording;
		OutputPathTextBox.IsEnabled = !isRecording;
		BrowseButton.IsEnabled = !isRecording;
		StartButton.IsEnabled = !isRecording && !isReviewMode;
		StopButton.IsEnabled = isRecording;
	}

	private void UpdateControls()
	{
		var isRecording = recording is not null;
		ProcessGrid.IsEnabled = !isBusy && !isRecording && !isReviewMode;
		FilterTextBox.IsEnabled = !isBusy && !isRecording && !isReviewMode;
		RefreshButton.IsEnabled = !isBusy && !isRecording;
		OutputPathTextBox.IsEnabled = !isBusy && !isRecording;
		BrowseButton.IsEnabled = !isBusy && !isRecording;
		StartButton.IsEnabled = !isBusy && !isRecording && !isReviewMode;
		StopButton.IsEnabled = !isBusy && isRecording;
	}

	private void SetReviewMode(bool isReview)
	{
		isReviewMode = isReview;
		ProcessGrid.Visibility = isReview ? Visibility.Collapsed : Visibility.Visible;
		ReviewGrid.Visibility = isReview ? Visibility.Visible : Visibility.Collapsed;
		NavigationPanel.Visibility = isReview ? Visibility.Visible : Visibility.Collapsed;
		UpdateControls();
	}

	private void FrameListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (FrameListBox.SelectedItem is RecordingFrameViewModel frame && !ReferenceEquals(reviewModel.SelectedFrame, frame))
			reviewModel.SelectFrame(frame);
	}

	private void SemanticTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
		reviewModel.SelectedTreeNode = e.NewValue as SemanticTreeNodeViewModel;

	private void PreviousFrame_Click(object sender, RoutedEventArgs e)
	{
		reviewModel.SelectPrevious();
		if (reviewModel.SelectedFrame is not null)
			FrameListBox.ScrollIntoView(reviewModel.SelectedFrame);
	}

	private void NextFrame_Click(object sender, RoutedEventArgs e)
	{
		reviewModel.SelectNext();
		if (reviewModel.SelectedFrame is not null)
			FrameListBox.ScrollIntoView(reviewModel.SelectedFrame);
	}

	private void JumpLatest_Click(object sender, RoutedEventArgs e)
	{
		reviewModel.JumpToLatest();
		if (reviewModel.SelectedFrame is not null)
			FrameListBox.ScrollIntoView(reviewModel.SelectedFrame);
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
		return Path.Combine(directory, $"recording-{DateTime.Now:yyyyMMdd-HHmmss}.dft.txt");
	}

	public sealed record ProcessRow(int ProcessId, string DisplayName, string WindowTitle);
}
