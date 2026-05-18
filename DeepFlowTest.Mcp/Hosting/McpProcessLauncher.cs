namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.Diagnostics;
using System.IO;
using DeepFlowTest;
using DeepFlowTest.Mcp.Contracts;

internal interface IMcpProcessLauncher
{
	IMcpLaunchedProcess Start(McpLaunchOptions options);
}

internal interface IMcpLaunchedProcess : ITargetProcess
{
	string? MainWindowTitle { get; }

	void Refresh();

	void Kill(bool entireProcessTree);
}

internal sealed class McpProcessLauncher : IMcpProcessLauncher
{
	public IMcpLaunchedProcess Start(McpLaunchOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.FileName))
			throw new ArgumentException("Launch file name is required.", nameof(options));

		var startInfo = new ProcessStartInfo
		{
			FileName = options.FileName,
			Arguments = options.Arguments ?? string.Empty,
			UseShellExecute = false,
			WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
				? Path.GetDirectoryName(Path.GetFullPath(options.FileName)) ?? Environment.CurrentDirectory
				: options.WorkingDirectory!,
		};

		foreach (var item in options.EnvironmentVariables)
		{
			if (item.Value is null)
				startInfo.Environment.Remove(item.Key);
			else
				startInfo.Environment[item.Key] = item.Value;
		}

		var process = Process.Start(startInfo)
			?? throw new InvalidOperationException($"Could not start process '{options.FileName}'.");
		return new McpLaunchedProcess(process);
	}
}

internal sealed class McpLaunchedProcess : IMcpLaunchedProcess
{
	private readonly Process process;

	public McpLaunchedProcess(Process process)
	{
		this.process = process ?? throw new ArgumentNullException(nameof(process));
	}

	public int Id => process.Id;

	public string ProcessName => process.ProcessName;

	public string? MainWindowTitle => process.MainWindowTitle;

	public bool HasExited => process.HasExited;

	public int? ExitCode
	{
		get
		{
			try
			{
				Refresh();
				return process.HasExited ? process.ExitCode : null;
			}
			catch (InvalidOperationException)
			{
				return null;
			}
		}
	}

	public void Refresh() => process.Refresh();

	public void Kill() => process.Kill();

	public void Kill(bool entireProcessTree) => process.Kill(entireProcessTree);

	public void Dispose() => process.Dispose();
}
