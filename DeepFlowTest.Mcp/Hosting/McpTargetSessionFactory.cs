namespace DeepFlowTest.Mcp.Hosting;

using System;
using System.IO;
using DeepFlowTest;
using DeepFlowTest.Automation;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using Microsoft.Extensions.Options;

internal sealed class McpTargetSessionFactory
{
	private readonly AutomationServices services;
	private readonly IMcpProcessLauncher launcher;
	private readonly IOptions<McpServerOptions> options;

	public McpTargetSessionFactory(AutomationServices services, IMcpProcessLauncher launcher, IOptions<McpServerOptions> options)
	{
		this.services = services ?? throw new ArgumentNullException(nameof(services));
		this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
	}

	public McpSession Attach(McpTargetSelector selector, int? timeoutMs = null, bool noInject = false, string? pipeId = null)
	{
		ArgumentNullException.ThrowIfNull(selector);
		if (selector.IsEmpty)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "A target selector is required.");

		var target = services.TargetResolver.Resolve(selector.ToAutomationSelector());
		var session = services.SessionService.Open(target, CreateAttachOptions(timeoutMs, noInject, pipeId));
		return new McpSession(target, session, "attach");
	}

	public McpSession Launch(McpLaunchOptions launchOptions)
	{
		ArgumentNullException.ThrowIfNull(launchOptions);
		if (!options.Value.Policy.AllowLaunch)
			throw new AutomationException(AutomationErrorCodes.ActionDenied, "Launching applications requires allowLaunch policy.");
		if (string.IsNullOrWhiteSpace(launchOptions.FileName))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Launch fileName is required.");

		McpArgumentParsing.ValidateExecutableAllowed(launchOptions.FileName, options.Value.Policy.AllowedExecutableRoots);
		var process = launcher.Start(launchOptions);
		try
		{
			process.Refresh();
			if (process.HasExited)
				throw new AutomationException(AutomationErrorCodes.TargetExited, $"Launched process {process.Id} exited before attach.");

			var target = new TargetInfo
			{
				ProcessId = process.Id,
				ProcessName = SafeProcessName(process, launchOptions.FileName),
				MainWindowTitle = SafeMainWindowTitle(process),
				TargetProcess = process,
			};
			var session = services.SessionService.Open(
				target,
				CreateAttachOptions(launchOptions.AttachTimeoutMs, noInject: false, pipeId: null));
			return new McpSession(target, session, "launch", process, launchOptions.TerminateOnDetach);
		}
		catch
		{
			if (launchOptions.TerminateOnDetach)
				TryKill(process);
			else
				process.Dispose();
			throw;
		}
	}

	private AutomationAttachOptions CreateAttachOptions(int? timeoutMs, bool noInject, string? pipeId) =>
		new()
		{
			TimeoutMs = Math.Max(1, timeoutMs ?? options.Value.AttachTimeoutMs),
			NoInject = noInject,
			PipeId = pipeId,
		};

	private static string SafeProcessName(IMcpLaunchedProcess process, string fileName)
	{
		try
		{
			return process.ProcessName;
		}
		catch (InvalidOperationException)
		{
			return Path.GetFileNameWithoutExtension(fileName);
		}
	}

	private static string? SafeMainWindowTitle(IMcpLaunchedProcess process)
	{
		try
		{
			return process.MainWindowTitle;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private static void TryKill(IMcpLaunchedProcess process)
	{
		try
		{
			process.Refresh();
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
		catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
		}
		finally
		{
			process.Dispose();
		}
	}
}
