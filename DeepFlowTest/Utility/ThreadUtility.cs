namespace DeepFlowTest.Utility;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

public static class ThreadUtility
{
	public static UiThreadRunResult RunOnUIThread(Action action)
	{
		_ = action ?? throw new ArgumentNullException(nameof(action));

		var dispatcher = FindWpfDispatcher();
		if (dispatcher is not null)
		{
			RunOnDispatcher(dispatcher, action);
			return UiThreadRunResult.Finished;
		}

		var control = FindWinFormsControl();
		if (control is not null)
		{
			RunOnWinFormsControl(control, action);
			return UiThreadRunResult.Finished;
		}

		return UiThreadRunResult.Unable;
	}

	public static async Task<UiThreadRunResult> RunOnUIThreadAsync(Func<Task> action)
	{
		_ = action ?? throw new ArgumentNullException(nameof(action));

		var dispatcher = FindWpfDispatcher();
		if (dispatcher is not null)
		{
			await RunOnDispatcherAsync(dispatcher, action).ConfigureAwait(false);
			return UiThreadRunResult.Finished;
		}

		var control = FindWinFormsControl();
		if (control is not null)
		{
			await RunOnWinFormsControlAsync(control, action).ConfigureAwait(false);
			return UiThreadRunResult.Finished;
		}

		return UiThreadRunResult.Unable;
	}

	public static bool HasSupportedUiRoot()
	{
		var availability = GetAvailability();
		return availability.IsWpfAvailable || availability.IsWinFormsAvailable;
	}

	public static UiAvailability GetAvailability()
	{
		var wpfRootCount = GetWpfRootCount();
		var winFormsRootCount = GetWinFormsRootCount();
		return new UiAvailability
		{
			IsWpfAvailable = wpfRootCount > 0,
			IsWinFormsAvailable = winFormsRootCount > 0,
			IsNativeFallbackAvailable = true,
			IsDispatcherAvailable = FindWpfDispatcher() is not null,
			RootCount = wpfRootCount + winFormsRootCount,
		};
	}

	public static void RunOnDispatcher(Dispatcher dispatcher, Action action)
	{
		_ = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_ = action ?? throw new ArgumentNullException(nameof(action));

		if (dispatcher.CheckAccess())
			action();
		else
			dispatcher.Invoke(action);
	}

	public static Task RunOnDispatcherAsync(Dispatcher dispatcher, Func<Task> action)
	{
		_ = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_ = action ?? throw new ArgumentNullException(nameof(action));

		if (dispatcher.CheckAccess())
			return action();

		return RunDispatchedAsync(dispatcher, action);
	}

	public static async Task<T> TimeoutAfter<T>(Task<T> task, TimeSpan timeout)
	{
		using var timeoutSource = new CancellationTokenSource();
		var completed = await Task.WhenAny(task, Task.Delay(timeout, timeoutSource.Token)).ConfigureAwait(false);
		if (completed == task)
		{
			timeoutSource.Cancel();
			return await task.ConfigureAwait(false);
		}

		throw new TimeoutException($"Command did not finish within {timeout.TotalMilliseconds:0} ms.");
	}

	private static async Task RunDispatchedAsync(Dispatcher dispatcher, Func<Task> action)
	{
		var innerTask = await dispatcher.InvokeAsync(action).Task.ConfigureAwait(false);
		await innerTask.ConfigureAwait(false);
	}

	private static void RunOnWinFormsControl(WinForms.Control control, Action action)
	{
		if (control.IsDisposed)
			return;

		if (control.InvokeRequired)
			control.Invoke(action);
		else
			action();
	}

	private static async Task RunOnWinFormsControlAsync(WinForms.Control control, Func<Task> action)
	{
		if (control.IsDisposed)
			return;

		if (!control.InvokeRequired)
		{
			await action().ConfigureAwait(false);
			return;
		}

		var taskSource = new TaskCompletionSource<Task>();
		control.BeginInvoke(new Action(() =>
		{
			try
			{
				taskSource.SetResult(action());
			}
			catch (Exception ex)
			{
				taskSource.SetException(ex);
			}
		}));

		await (await taskSource.Task.ConfigureAwait(false)).ConfigureAwait(false);
	}

	private static Dispatcher? FindWpfDispatcher()
	{
		if (Application.Current?.Dispatcher is not null)
			return Application.Current.Dispatcher;

		foreach (PresentationSource? source in PresentationSource.CurrentSources)
		{
			if (source?.Dispatcher is not null)
				return source.Dispatcher;
		}

		return null;
	}

	private static WinForms.Control? FindWinFormsControl()
	{
		try
		{
			return WinForms.Application.OpenForms
				.Cast<WinForms.Form?>()
				.FirstOrDefault(form => form is not null && !form.IsDisposed && form.IsHandleCreated);
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private static int GetWpfRootCount()
	{
		try
		{
			return PresentationSource.CurrentSources.Cast<PresentationSource?>().Count(source => source?.RootVisual is not null);
		}
		catch (InvalidOperationException)
		{
			return 0;
		}
	}

	private static int GetWinFormsRootCount()
	{
		try
		{
			return WinForms.Application.OpenForms.Cast<WinForms.Form?>().Count(form => form is not null && !form.IsDisposed);
		}
		catch (InvalidOperationException)
		{
			return 0;
		}
	}
}

public enum UiThreadRunResult
{
	Unable,
	Finished,
	Pending,
}

public sealed class UiAvailability
{
	public bool IsWpfAvailable { get; set; }

	public bool IsWinFormsAvailable { get; set; }

	public bool IsNativeFallbackAvailable { get; set; }

	public bool IsDispatcherAvailable { get; set; }

	public int RootCount { get; set; }
}
