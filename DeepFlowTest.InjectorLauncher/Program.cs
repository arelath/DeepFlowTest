namespace DeepFlowTest.InjectorLauncher;

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using DeepFlowTest.Shared;

public static class Program
{
#if !DEEPFLOWTEST_INJECTOR_TESTS
	public static int Main(string[] args)
	{
		return Run(args);
	}
#endif

	internal static int Run(string[] args)
	{
		InjectorLog.Reset();

		if (!InjectorLauncherCommandLineOptions.TryParse(args, out var options, out var error))
		{
			InjectorLog.Write(error);
			return InjectorExitCode.InvalidArguments;
		}

		if (options.HelpRequested)
		{
			Console.WriteLine(InjectorLauncherCommandLineOptions.HelpText);
			return InjectorExitCode.Success;
		}

		try
		{
			if (options.Debug)
				Debugger.Launch();

			if (options.AttachConsoleToParent)
				ConsoleHelper.AttachConsoleToParentProcessOrAllocateNewOne();

			var targetWindowHandle = new IntPtr(options.TargetWindowHandle);
			using var processWrapper = options.HasTargetProcessId
				? ProcessWrapper.From(options.TargetProcessId, targetWindowHandle)
				: ProcessWrapper.FromWindowHandle(targetWindowHandle);
			if (processWrapper is null)
				return InjectorExitCode.TargetNotFound;

			var currentArchitecture = ArchitectureDetector.CurrentProcessArchitecture;
			if (!processWrapper.Architecture.Equals(currentArchitecture, StringComparison.Ordinal))
			{
				var redirect = ArchitectureRedirect.CreateStartInfo(GetCurrentExecutablePath(), currentArchitecture, processWrapper.Architecture, args);
				if (redirect is not null)
					return ArchitectureRedirect.Run(redirect);
			}

			var payloadPath = ResolvePayloadAssemblyPath(options.Assembly, processWrapper.SupportedFrameworkFamily, options.PayloadRoot);
			var injectorData = new InjectorData
			{
				FullAssemblyPath = payloadPath,
				ClassName = options.ClassName,
				MethodName = options.MethodName,
				StartupArgument = options.StartupArgument,
			};

			Injector.InjectIntoProcess(processWrapper, injectorData);
			return InjectorExitCode.Success;
		}
		catch (InjectorLauncherException ex)
		{
			InjectorLog.Write($"{ex.ExitCode}: {ex.Message}");
			return ex.ExitCode;
		}
		catch (FileNotFoundException ex)
		{
			InjectorLog.Write(ex.ToString());
			return InjectorExitCode.MissingPayload;
		}
		catch (Exception ex)
		{
			InjectorLog.Write(ex.ToString());
			return InjectorExitCode.UnexpectedFailure;
		}
	}

	private static string ResolvePayloadAssemblyPath(string assemblyNameOrPath, string frameworkFamily, string payloadRootOverride)
	{
		var rootDirectory = string.IsNullOrWhiteSpace(payloadRootOverride)
			? InjectorPathResolver.RootDirectory
			: payloadRootOverride;
		return InjectorPathResolver.ResolvePayloadPath(rootDirectory, frameworkFamily, assemblyNameOrPath);
	}

	private static string GetCurrentExecutablePath()
	{
		return Process.GetCurrentProcess().MainModule?.FileName ?? Assembly.GetExecutingAssembly().Location;
	}
}
