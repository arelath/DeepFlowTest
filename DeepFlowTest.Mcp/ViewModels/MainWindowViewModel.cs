namespace DeepFlowTest.Mcp.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Activity;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using Microsoft.Extensions.Options;

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
	private readonly McpSessionHost sessionHost;
	private readonly DeepFlowMcpHost serverHost;
	private readonly McpStreamRegistry streamRegistry;
	private readonly McpEndpointReporter endpointReporter;
	private readonly McpActivityStore activityStore;
	private readonly IOptions<McpServerOptions> options;
	private readonly Dispatcher dispatcher;
	private readonly DispatcherTimer refreshTimer;
	private McpEndpointInfo endpointInfo = new();
	private ActivityEventViewModel? selectedActivity;
	private string? lastError;
	private string attachPidText = string.Empty;
	private string? attachProcessName;
	private string? attachWindowTitle;
	private string? launchPath;
	private string? launchArguments;
	private bool terminateOnDetach;
	private bool virtualPointerEnabled;
	private bool virtualPointerClickRipples = true;
	private bool virtualPointerDragTrail = true;
	private bool virtualPointerInScreenshots;
	private string virtualPointerHideDelayMs = "800";
	private string? activityFilter;

	public MainWindowViewModel(
		McpSessionHost sessionHost,
		DeepFlowMcpHost serverHost,
		McpStreamRegistry streamRegistry,
		McpEndpointReporter endpointReporter,
		McpActivityStore activityStore,
		IOptions<McpServerOptions> options)
	{
		this.sessionHost = sessionHost ?? throw new ArgumentNullException(nameof(sessionHost));
		this.serverHost = serverHost ?? throw new ArgumentNullException(nameof(serverHost));
		this.streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
		this.endpointReporter = endpointReporter ?? throw new ArgumentNullException(nameof(endpointReporter));
		this.activityStore = activityStore ?? throw new ArgumentNullException(nameof(activityStore));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

		StartServerCommand = new AsyncRelayCommand(StartServerAsync, () => !serverHost.IsRunning);
		StopServerCommand = new AsyncRelayCommand(StopServerAsync, () => serverHost.IsRunning);
		PanicDetachCommand = new RelayCommand(PanicDetach);
		CopyUrlCommand = new RelayCommand(CopyUrl, () => !string.IsNullOrWhiteSpace(StreamableHttpUrl));
		AttachCommand = new RelayCommand(AttachTarget);
		LaunchCommand = new RelayCommand(LaunchTarget);
		ApplyVirtualPointerCommand = new RelayCommand(ApplyVirtualPointer);

		endpointInfo = endpointReporter.Current;
		foreach (var activity in activityStore.Snapshot())
			ActivityEvents.Add(new ActivityEventViewModel(activity));

		endpointReporter.Changed += (_, info) => Dispatch(() =>
		{
			endpointInfo = info;
			OnPropertyChanged(nameof(ServerStateText));
			OnPropertyChanged(nameof(StreamableHttpUrl));
			OnPropertyChanged(nameof(StatusLine));
			RaiseCommandStates();
		});
		activityStore.ActivityPublished += (_, activity) => Dispatch(() =>
		{
			var item = new ActivityEventViewModel(activity);
			if (MatchesFilter(item))
				ActivityEvents.Add(item);
		});

		refreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
		{
			Interval = TimeSpan.FromMilliseconds(600),
		};
		refreshTimer.Tick += (_, _) => RefreshTargetState();
		refreshTimer.Start();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public ObservableCollection<ActivityEventViewModel> ActivityEvents { get; } = [];

	public ObservableCollection<string> ActiveStreams { get; } = [];

	public AsyncRelayCommand StartServerCommand { get; }

	public AsyncRelayCommand StopServerCommand { get; }

	public RelayCommand PanicDetachCommand { get; }

	public RelayCommand CopyUrlCommand { get; }

	public RelayCommand AttachCommand { get; }

	public RelayCommand LaunchCommand { get; }

	public RelayCommand ApplyVirtualPointerCommand { get; }

	public string ServerStateText => endpointInfo.State switch
	{
		"running" => "Server running",
		"starting" => "Server starting",
		"failed" => "Server failed",
		_ => "Server stopped",
	};

	public string StreamableHttpUrl => endpointInfo.StreamableHttpUrl ?? "Starting...";

	public string StatusLine => $"{ServerStateText} | {TargetSummary}";

	public string TargetSummary
	{
		get
		{
			var status = sessionHost.Status;
			if (!status.Attached)
				return "No target attached.";

			return $"{status.ProcessName} ({status.ProcessId}) | {status.MainWindowTitle} | {status.FrameworkFamily}";
		}
	}

	public string? LastError
	{
		get => lastError;
		set
		{
			if (lastError == value)
				return;

			lastError = value;
			OnPropertyChanged();
		}
	}

	public ActivityEventViewModel? SelectedActivity
	{
		get => selectedActivity;
		set
		{
			if (selectedActivity == value)
				return;

			selectedActivity = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(SelectedActivityDetails));
		}
	}

	public string SelectedActivityDetails => selectedActivity?.DetailsText ?? string.Empty;

	public string AttachPidText
	{
		get => attachPidText;
		set => SetField(ref attachPidText, value);
	}

	public string? AttachProcessName
	{
		get => attachProcessName;
		set => SetField(ref attachProcessName, value);
	}

	public string? AttachWindowTitle
	{
		get => attachWindowTitle;
		set => SetField(ref attachWindowTitle, value);
	}

	public string? LaunchPath
	{
		get => launchPath;
		set => SetField(ref launchPath, value);
	}

	public string? LaunchArguments
	{
		get => launchArguments;
		set => SetField(ref launchArguments, value);
	}

	public bool TerminateOnDetach
	{
		get => terminateOnDetach;
		set => SetField(ref terminateOnDetach, value);
	}

	public bool AllowLaunch
	{
		get => options.Value.Policy.AllowLaunch;
		set
		{
			if (options.Value.Policy.AllowLaunch == value)
				return;

			options.Value.Policy.AllowLaunch = value;
			OnPropertyChanged();
		}
	}

	public bool AllowActions
	{
		get => options.Value.Policy.AllowActions;
		set
		{
			if (options.Value.Policy.AllowActions == value)
				return;

			options.Value.Policy.AllowActions = value;
			OnPropertyChanged();
		}
	}

	public bool AllowArbitraryInvoke
	{
		get => options.Value.Policy.AllowArbitraryInvoke;
		set
		{
			if (options.Value.Policy.AllowArbitraryInvoke == value)
				return;

			options.Value.Policy.AllowArbitraryInvoke = value;
			OnPropertyChanged();
		}
	}

	public bool AllowFileWrites
	{
		get => options.Value.Policy.AllowFileWrites;
		set
		{
			if (options.Value.Policy.AllowFileWrites == value)
				return;

			options.Value.Policy.AllowFileWrites = value;
			OnPropertyChanged();
		}
	}

	public bool VirtualPointerEnabled
	{
		get => virtualPointerEnabled;
		set => SetField(ref virtualPointerEnabled, value);
	}

	public bool VirtualPointerClickRipples
	{
		get => virtualPointerClickRipples;
		set => SetField(ref virtualPointerClickRipples, value);
	}

	public bool VirtualPointerDragTrail
	{
		get => virtualPointerDragTrail;
		set => SetField(ref virtualPointerDragTrail, value);
	}

	public bool VirtualPointerInScreenshots
	{
		get => virtualPointerInScreenshots;
		set => SetField(ref virtualPointerInScreenshots, value);
	}

	public string VirtualPointerHideDelayMs
	{
		get => virtualPointerHideDelayMs;
		set => SetField(ref virtualPointerHideDelayMs, value);
	}

	public string? ActivityFilter
	{
		get => activityFilter;
		set
		{
			if (activityFilter == value)
				return;

			activityFilter = value;
			OnPropertyChanged();
			RefreshActivityFilter();
		}
	}

	private async Task StartServerAsync()
	{
		try
		{
			LastError = null;
			await serverHost.StartAsync();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			LastError = ex.Message;
		}
	}

	private async Task StopServerAsync()
	{
		try
		{
			LastError = null;
			await serverHost.StopAsync();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			LastError = ex.Message;
		}
	}

	private void PanicDetach()
	{
		try
		{
			LastError = null;
			sessionHost.Detach();
			RefreshTargetState();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			LastError = ex.Message;
		}
	}

	private void CopyUrl()
	{
		if (!string.IsNullOrWhiteSpace(endpointInfo.StreamableHttpUrl))
			Clipboard.SetText(endpointInfo.StreamableHttpUrl);
	}

	private void AttachTarget()
	{
		try
		{
			LastError = null;
			int? pid = string.IsNullOrWhiteSpace(AttachPidText) ? null : int.Parse(AttachPidText, System.Globalization.CultureInfo.InvariantCulture);
			sessionHost.Attach(new McpTargetSelector
			{
				ProcessId = pid,
				ProcessName = string.IsNullOrWhiteSpace(AttachProcessName) ? null : AttachProcessName,
				WindowTitle = string.IsNullOrWhiteSpace(AttachWindowTitle) ? null : AttachWindowTitle,
			});
			RefreshTargetState();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			LastError = ex.Message;
		}
	}

	private void LaunchTarget()
	{
		try
		{
			LastError = null;
			sessionHost.Launch(new McpLaunchOptions
			{
				FileName = LaunchPath ?? string.Empty,
				Arguments = LaunchArguments,
				TerminateOnDetach = TerminateOnDetach,
			});
			RefreshTargetState();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			LastError = ex.Message;
		}
	}

	private void ApplyVirtualPointer()
	{
		try
		{
			LastError = null;
			if (!int.TryParse(VirtualPointerHideDelayMs, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var hideDelay))
				throw new InvalidOperationException("Hide delay must be an integer.");

			var pointer = new VirtualPointerOptionsDto
			{
				Enabled = VirtualPointerEnabled,
				ShowClickRipples = VirtualPointerClickRipples,
				ShowDragTrail = VirtualPointerDragTrail,
				HideDelayMs = hideDelay,
				IncludeInScreenshots = VirtualPointerInScreenshots,
			};
			sessionHost.Send<object>(new ConfigureDiagnosticsCommandRequest { VirtualPointer = pointer }, options.Value.DefaultTimeoutMs);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			LastError = ex.Message;
		}
	}

	private void RefreshTargetState()
	{
		OnPropertyChanged(nameof(TargetSummary));
		OnPropertyChanged(nameof(StatusLine));
		RefreshActiveStreams();
	}

	private void RefreshActiveStreams()
	{
		var active = streamRegistry.ListActiveStreams();
		ActiveStreams.Clear();
		foreach (var stream in active)
			ActiveStreams.Add(stream);
	}

	private void RefreshActivityFilter()
	{
		ActivityEvents.Clear();
		foreach (var activity in activityStore.Snapshot())
		{
			var item = new ActivityEventViewModel(activity);
			if (MatchesFilter(item))
				ActivityEvents.Add(item);
		}
	}

	private bool MatchesFilter(ActivityEventViewModel item)
	{
		if (string.IsNullOrWhiteSpace(activityFilter))
			return true;

		var filter = activityFilter.Trim();
		return Contains(item.Source, filter)
			|| Contains(item.Kind, filter)
			|| Contains(item.Name, filter)
			|| Contains(item.Status, filter)
			|| Contains(item.Summary, filter);
	}

	private static bool Contains(string? value, string filter) =>
		value?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

	private void RaiseCommandStates()
	{
		StartServerCommand.RaiseCanExecuteChanged();
		StopServerCommand.RaiseCanExecuteChanged();
		CopyUrlCommand.RaiseCanExecuteChanged();
	}

	private void Dispatch(Action action)
	{
		if (dispatcher.CheckAccess())
			action();
		else
			dispatcher.InvokeAsync(action);
	}

	private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (Equals(field, value))
			return false;

		field = value;
		OnPropertyChanged(propertyName);
		return true;
	}

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
