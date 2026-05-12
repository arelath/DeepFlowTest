namespace DeepFlowTest;

using System;
using System.Diagnostics;
using DeepFlowTest.Utility;

public sealed class AppConnection : IDisposable
{
	private bool disposed;

	public AppConnection(AppConnectionOptions options)
	{
		_ = options ?? throw new ArgumentNullException(nameof(options));
		TargetProcess = options.TargetProcess ?? throw new ArgumentNullException(nameof(options.TargetProcess));
		OwnsProcess = options.OwnsProcess;
		ReusesPipe = options.ReusesPipe;
		PipeName = string.IsNullOrWhiteSpace(options.PipeName) ? throw new ArgumentException("Pipe name is required.", nameof(options)) : options.PipeName;
		PayloadFrameworkFamily = options.PayloadFrameworkFamily ?? string.Empty;
		InjectorState = options.InjectorState;
		if (OwnsProcess)
			RegisterOwnedProcessForParentClose(TargetProcess);
	}

	public ITargetProcess TargetProcess { get; }

	public bool OwnsProcess { get; }

	public bool ReusesPipe { get; private set; }

	public string PipeName { get; }

	public string PayloadFrameworkFamily { get; private set; }

	public AppConnectionInjectorState InjectorState { get; private set; }

	public string? LastStartupLog { get; private set; }

	public bool IsDisposed => disposed;

	internal static Action<ITargetProcess> RegisterOwnedProcessForParentClose { get; set; } = RegisterTargetProcessWithParentCloseTracker;

	public void EnsurePipeOrInject(Func<AppConnection, bool> isPipeAvailable, IAppConnectionInjector injector, bool allowInjection)
	{
		ThrowIfDisposed();
		_ = isPipeAvailable ?? throw new ArgumentNullException(nameof(isPipeAvailable));
		_ = injector ?? throw new ArgumentNullException(nameof(injector));

		if (isPipeAvailable(this))
		{
			ReusesPipe = true;
			InjectorState = AppConnectionInjectorState.InjectionSkipped;
			return;
		}

		ReusesPipe = false;
		if (!allowInjection)
		{
			InjectorState = AppConnectionInjectorState.InjectionSkipped;
			return;
		}

		InjectorState = AppConnectionInjectorState.Injecting;
		try
		{
			var result = injector.Inject(this);
			InjectorState = AppConnectionInjectorState.Injected;
			PayloadFrameworkFamily = result.PayloadFrameworkFamily ?? PayloadFrameworkFamily;
			LastStartupLog = result.StartupLogTail;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			InjectorState = AppConnectionInjectorState.Failed;
			LastStartupLog = injector.TryReadStartupLog(this);
			throw new AppConnectionException("Target injection failed.", ex, LastStartupLog);
		}
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		if (OwnsProcess && !TargetProcess.HasExited)
			TargetProcess.Kill();

		TargetProcess.Dispose();
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
			throw new ObjectDisposedException(nameof(AppConnection));
	}

	private static void RegisterTargetProcessWithParentCloseTracker(ITargetProcess process)
	{
		if (process is TargetProcess targetProcess)
			ProcessCloseOnParentClose.Add(targetProcess.Process);
	}

	public static AppConnection ForLaunch(ITargetProcess process, string pipeName, string payloadFrameworkFamily = "") =>
		new(new AppConnectionOptions
		{
			TargetProcess = process,
			OwnsProcess = true,
			PipeName = pipeName,
			PayloadFrameworkFamily = payloadFrameworkFamily,
		});

	public static AppConnection ForAttach(ITargetProcess process, string pipeName, string payloadFrameworkFamily = "") =>
		new(new AppConnectionOptions
		{
			TargetProcess = process,
			OwnsProcess = false,
			PipeName = pipeName,
			PayloadFrameworkFamily = payloadFrameworkFamily,
		});
}

public sealed class AppConnectionOptions
{
	public ITargetProcess? TargetProcess { get; set; }

	public bool OwnsProcess { get; set; }

	public bool ReusesPipe { get; set; }

	public string PipeName { get; set; } = string.Empty;

	public string PayloadFrameworkFamily { get; set; } = string.Empty;

	public AppConnectionInjectorState InjectorState { get; set; } = AppConnectionInjectorState.NotInjected;
}

public enum AppConnectionInjectorState
{
	NotInjected,
	Injecting,
	Injected,
	InjectionSkipped,
	Failed,
}

public interface IAppConnectionInjector
{
	AppConnectionInjectionResult Inject(AppConnection connection);

	string? TryReadStartupLog(AppConnection connection);
}

public sealed class AppConnectionInjectionResult
{
	public string? PayloadFrameworkFamily { get; set; }

	public string? StartupLogTail { get; set; }
}

public sealed class AppConnectionException : Exception
{
	public AppConnectionException(string message, Exception innerException, string? startupLogTail)
		: base(message, innerException)
	{
		StartupLogTail = startupLogTail;
	}

	public string? StartupLogTail { get; }
}

public interface ITargetProcess : IDisposable
{
	int Id { get; }

	string ProcessName { get; }

	bool HasExited { get; }

	void Kill();
}

public sealed class TargetProcess : ITargetProcess
{
	private readonly Process process;

	public TargetProcess(Process process)
	{
		this.process = process ?? throw new ArgumentNullException(nameof(process));
	}

	public Process Process => process;

	public int Id => process.Id;

	public string ProcessName => process.ProcessName;

	public bool HasExited => process.HasExited;

	public void Kill() => process.Kill();

	public void Dispose() => process.Dispose();
}
