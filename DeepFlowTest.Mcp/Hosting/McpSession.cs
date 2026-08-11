namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Diagnostics;
using DeepFlowTest.Cli;

internal sealed class McpSession : IDisposable
{
	private bool disposed;

	public McpSession(
		TargetInfo target,
		ICliAppSession session,
		string source,
		IMcpLaunchedProcess? launchedProcess = null,
		bool terminateOnDetach = false)
	{
		Target = target ?? throw new ArgumentNullException(nameof(target));
		AppSession = session ?? throw new ArgumentNullException(nameof(session));
		Source = source;
		LaunchedProcess = launchedProcess;
		TerminateOnDetach = terminateOnDetach;
	}

	public Guid SessionId { get; } = Guid.NewGuid();

	public TargetInfo Target { get; }

	public ICliAppSession AppSession { get; }

	public DateTimeOffset AttachedAtUtc { get; } = DateTimeOffset.UtcNow;

	public string Source { get; }

	public IMcpLaunchedProcess? LaunchedProcess { get; }

	public bool LaunchedByServer => LaunchedProcess is not null;

	public bool TerminateOnDetach { get; }

	public bool IsAlive
	{
		get
		{
			try
			{
				if (LaunchedProcess is not null)
				{
					LaunchedProcess.Refresh();
					return !LaunchedProcess.HasExited;
				}

				var process = Target.OpenProcess();
				try
				{
					return !process.HasExited;
				}
				finally
				{
					if (!ReferenceEquals(process, Target.TargetProcess))
						process.Dispose();
				}
			}
			catch (Exception ex) when (ex is CliException or InvalidOperationException or System.ComponentModel.Win32Exception)
			{
				return false;
			}
		}
	}

	public int? ExitCode
	{
		get
		{
			try
			{
				if (LaunchedProcess is not null)
				{
					LaunchedProcess.Refresh();
					return LaunchedProcess.HasExited ? LaunchedProcess.ExitCode : null;
				}

				var process = Target.OpenProcess();
				try
				{
					return process.ExitCode;
				}
				finally
				{
					if (!ReferenceEquals(process, Target.TargetProcess))
						process.Dispose();
				}
			}
			catch (Exception ex) when (ex is CliException or InvalidOperationException or System.ComponentModel.Win32Exception)
			{
				return null;
			}
		}
	}

	public string? GetMainWindowTitle()
	{
		try
		{
			if (LaunchedProcess is not null)
			{
				LaunchedProcess.Refresh();
				return LaunchedProcess.MainWindowTitle;
			}

			if (Target.TargetProcess is IMcpLaunchedProcess launched)
			{
				launched.Refresh();
				return launched.MainWindowTitle;
			}

			using var process = Process.GetProcessById(Target.ProcessId);
			process.Refresh();
			return process.MainWindowTitle;
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			return Target.MainWindowTitle;
		}
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		if (TerminateOnDetach && LaunchedProcess is not null)
		{
			try
			{
				LaunchedProcess.Refresh();
				if (!LaunchedProcess.HasExited)
					LaunchedProcess.Kill(entireProcessTree: true);
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
			{
			}
		}

		AppSession.Dispose();
		if (LaunchedProcess is not null && Target.TargetProcess is null)
			LaunchedProcess.Dispose();
	}
}
