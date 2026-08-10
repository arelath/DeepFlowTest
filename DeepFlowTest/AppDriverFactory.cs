namespace DeepFlowTest;

using System;
using System.Diagnostics;

public sealed class AppDriverFactory
{
	private readonly IAppDriverBackend backend;
	private readonly Func<AppConnection, AppDriverOptions, IUnsafeAppDriverCommandSession> sessionFactory;

	public AppDriverFactory(
		IAppDriverBackend? backend = null,
		Func<AppConnection, AppDriverOptions, IUnsafeAppDriverCommandSession>? sessionFactory = null)
	{
		this.backend = backend ?? new DefaultAppDriverBackend();
		this.sessionFactory = sessionFactory ?? ((connection, options) => new NamedPipeAppDriverCommandSession(connection, options));
	}

	public AppDriver Launch(string executablePath) =>
		Launch(executablePath, new AppDriverLaunchOptions());

	public AppDriver Launch(string executablePath, string? args) =>
		Launch(executablePath, new AppDriverLaunchOptions { Arguments = args });

	public AppDriver Launch(ProcessStartInfo processStartInfo)
	{
		_ = processStartInfo ?? throw new ArgumentNullException(nameof(processStartInfo));
		var options = new AppDriverLaunchOptions
		{
			Arguments = processStartInfo.Arguments,
			WorkingDirectory = string.IsNullOrWhiteSpace(processStartInfo.WorkingDirectory) ? null : processStartInfo.WorkingDirectory,
			ProcessStartInfo = processStartInfo,
		};
		options.Validate();
		return AppDriver.FromConnection(backend.Launch(processStartInfo.FileName, options), options, sessionFactory);
	}

	public AppDriver Launch(string executablePath, AppDriverLaunchOptions options)
	{
		_ = options ?? throw new ArgumentNullException(nameof(options));
		options.Validate();
		return AppDriver.FromConnection(backend.Launch(AppDriverLaunch.NormalizeExecutablePath(executablePath), options), options, sessionFactory);
	}

	public AppDriver AttachTo(int processId, AppDriverAttachOptions? options = null)
	{
		var effectiveOptions = options ?? new AppDriverAttachOptions();
		effectiveOptions.Validate();
		return AppDriver.FromConnection(backend.AttachTo(processId, effectiveOptions), effectiveOptions, sessionFactory);
	}

	public AppDriver AttachTo(string processName, AppDriverAttachOptions? options = null)
	{
		var effectiveOptions = options ?? new AppDriverAttachOptions();
		effectiveOptions.Validate();
		return AppDriver.FromConnection(backend.AttachTo(processName, effectiveOptions), effectiveOptions, sessionFactory);
	}
}
