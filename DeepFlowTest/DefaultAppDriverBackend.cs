namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;

public sealed class DefaultAppDriverBackend : IAppDriverBackend
{
	private readonly IProcessCatalog processCatalog;

	public DefaultAppDriverBackend()
		: this(new DefaultProcessCatalog())
	{
	}

	public DefaultAppDriverBackend(IProcessCatalog processCatalog)
	{
		this.processCatalog = processCatalog ?? throw new ArgumentNullException(nameof(processCatalog));
	}

	public AppConnection Launch(string executablePath, AppDriverLaunchOptions options)
	{
		_ = options ?? throw new ArgumentNullException(nameof(options));
		options.Validate();
		if (string.IsNullOrWhiteSpace(executablePath))
			throw new ArgumentException("Executable path is required.", nameof(executablePath));

		var startInfo = AppDriverLaunch.ResolveStartInfo(executablePath, options);
		var process = Process.Start(startInfo) ?? throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"Failed to start '{startInfo.FileName}'.");
		var connection = new AppConnection(new AppConnectionOptions
		{
			TargetProcess = new TargetProcess(process),
			OwnsProcess = options.OwnsProcess,
			PipeName = ResolvePipeName(options, process.Id),
		});

		InitializeConnection(connection, options, PayloadStartupModes.ReusableCli);
		return connection;
	}

	public AppConnection AttachTo(int processId, AppDriverAttachOptions options)
	{
		_ = options ?? throw new ArgumentNullException(nameof(options));
		options.Validate();
		var process = processCatalog.GetById(processId);
		var connection = AppConnection.ForAttach(process, ResolvePipeName(options, process.Id));
		InitializeConnection(connection, options, PayloadStartupModes.ReusableCli);
		return connection;
	}

	public AppConnection AttachTo(string processName, AppDriverAttachOptions options)
	{
		_ = options ?? throw new ArgumentNullException(nameof(options));
		options.Validate();
		var process = AppDriverProcessResolver.ResolveByName(processCatalog.GetProcesses(), processName, options.AllowContainsProcessNameMatch);
		var connection = AppConnection.ForAttach(process, ResolvePipeName(options, process.Id));
		InitializeConnection(connection, options, PayloadStartupModes.ReusableCli);
		return connection;
	}

	private static void InitializeConnection(AppConnection connection, AppDriverOptions options, string payloadMode)
	{
		connection.EnsurePipeOrInject(
			IsPipeAvailable,
			new ExternalInjectorAppConnectionInjector(options, payloadMode),
			options.AllowInjection);
	}

	private static bool IsPipeAvailable(AppConnection connection)
	{
		try
		{
			using var pipe = new NamedPipeClientStream(".", connection.PipeName, PipeDirection.InOut);
			pipe.Connect(TimeoutDefaults.PipeProbeConnectTimeoutMs);
			return true;
		}
		catch (TimeoutException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static string ResolvePipeName(AppDriverOptions options, int processId) =>
		string.IsNullOrWhiteSpace(options.PipeName)
			? $"{ProtocolConstants.PipePrefix}-{processId}"
			: options.PipeName!;
}

public interface IProcessCatalog
{
	ITargetProcess GetById(int processId);

	IReadOnlyList<ITargetProcess> GetProcesses();
}

public sealed class DefaultProcessCatalog : IProcessCatalog
{
	public ITargetProcess GetById(int processId)
	{
		try
		{
			return new TargetProcess(Process.GetProcessById(processId));
		}
		catch (ArgumentException ex)
		{
			throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"Process {processId} was not found.", ex);
		}
	}

	public IReadOnlyList<ITargetProcess> GetProcesses()
	{
		var processes = Process.GetProcesses();
		var result = new List<ITargetProcess>(processes.Length);
		foreach (var process in processes)
			result.Add(new TargetProcess(process));
		return result;
	}
}

public sealed class ExternalInjectorAppConnectionInjector : IAppConnectionInjector
{
	private readonly AppDriverOptions options;
	private readonly string payloadMode;
	private DateTimeOffset? injectorLogNotBefore;

	public ExternalInjectorAppConnectionInjector(AppDriverOptions options, string payloadMode)
	{
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.payloadMode = payloadMode ?? throw new ArgumentNullException(nameof(payloadMode));
	}

	public AppConnectionInjectionResult Inject(AppConnection connection)
	{
		if (!File.Exists(options.InjectorLauncherPath))
			throw new FileNotFoundException("Injector launcher was not found.", options.InjectorLauncherPath);

		var startupOptions = new AppDriverPayloadStartupOptions
		{
			PipeName = connection.PipeName,
			Mode = payloadMode,
			PayloadRoot = options.PayloadRoot,
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
		};
		var startInfo = new ProcessStartInfo(options.InjectorLauncherPath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			Arguments = BuildInjectorArguments(connection, startupOptions.Encode(), options.PayloadRoot),
		};
		injectorLogNotBefore = DateTimeOffset.UtcNow.AddSeconds(-1);
		using var injectorProcess = Process.Start(startInfo) ?? throw new AppDriverException(AppDriverErrorCodes.InjectorFailed, "Failed to start injector launcher.");
		if (!injectorProcess.WaitForExit((int)Math.Max(1, options.Timeout.TotalMilliseconds)))
		{
			try
			{
				injectorProcess.Kill();
			}
			catch (InvalidOperationException)
			{
			}

			throw new TimeoutException(AppDriverInjectionDiagnostics.AppendDiagnostics(
				$"Injector did not finish within {options.Timeout.TotalMilliseconds:0} ms.",
				TryReadStartupLog(connection)));
		}

		var startupLog = TryReadStartupLog(connection);
		if (injectorProcess.ExitCode != 0)
			throw new AppDriverException(
				AppDriverErrorCodes.InjectorFailed,
				AppDriverInjectionDiagnostics.AppendDiagnostics(
					$"Injector launcher exited with code {injectorProcess.ExitCode}.",
					startupLog));

		return new AppConnectionInjectionResult
		{
			PayloadFrameworkFamily = connection.PayloadFrameworkFamily,
			StartupLogTail = startupLog,
		};
	}

	public string? TryReadStartupLog(AppConnection connection)
	{
		return AppDriverInjectionDiagnostics.TryReadStartupLogTail(
			connection.PipeName,
			connection.TargetProcess.Id,
			injectorLogNotBefore);
	}

	internal static string BuildInjectorArguments(AppConnection connection, string startupArgument, string payloadRoot)
	{
		var parts = new List<string>(14)
		{
			Quote("--targetPID"),
			connection.TargetProcess.Id.ToString(CultureInfo.InvariantCulture),
			Quote("--assembly"),
			Quote(ProductInfo.Name),
			Quote("--className"),
			Quote("DeepFlowTest.AppDriverPayload.AppDriverPayload"),
			Quote("--methodName"),
			Quote("Start"),
			Quote("--startupArgument"),
			Quote(startupArgument),
		};

		if (!string.IsNullOrWhiteSpace(payloadRoot))
		{
			parts.Add(Quote("--payloadRoot"));
			parts.Add(Quote(payloadRoot));
		}

		return string.Join(" ", parts);
	}

	private static string Quote(string value)
	{
		var builder = new StringBuilder(value.Length + 2);
		builder.Append('"');
		var backslashCount = 0;
		foreach (var character in value)
		{
			if (character == '\\')
			{
				backslashCount++;
				continue;
			}

			if (character == '"')
			{
				builder.Append('\\', (backslashCount * 2) + 1);
				builder.Append('"');
				backslashCount = 0;
				continue;
			}

			builder.Append('\\', backslashCount);
			builder.Append(character);
			backslashCount = 0;
		}

		builder.Append('\\', backslashCount * 2);
		builder.Append('"');
		return builder.ToString();
	}
}
