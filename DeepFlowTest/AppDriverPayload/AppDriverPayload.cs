namespace DeepFlowTest.AppDriverPayload;

using System;
using DeepFlowTest.Contracts;

public static class AppDriverPayload
{
	private static IAppDriverPayloadRuntime runtime = new DefaultAppDriverPayloadRuntime();

	public static int Start(string startupArgument)
	{
		PayloadLog.Initialize("startup");
		PayloadLog.Write("Payload entry point called.");

		AppDriverPayloadStartupOptions options;
		try
		{
			options = AppDriverPayloadStartupOptions.Decode(startupArgument);
			PayloadLog.Initialize(options.PipeName);
			PayloadLog.Write($"Startup options parsed. Mode={options.Mode}; PayloadRoot={options.PayloadRoot}; ProtocolVersion={options.ProtocolVersion}.");
		}
		catch (Exception ex)
		{
			PayloadLog.Write("Payload startup option parsing failed.", ex);
			return 1;
		}

		try
		{
			var patchResult = AppHooks.Apply((message, exception) => PayloadLog.Write(message, exception));
			PayloadLog.Write($"Runtime patch diagnostics: {patchResult.Summary}.");

			if (!runtime.HasSupportedTarget())
			{
				PayloadLog.Write("Unsupported target: no WPF dispatcher or WinForms message loop was detected.");
				return 1;
			}

			if (options.Mode == PayloadStartupModes.ReusableCli)
			{
				runtime.StartReusable(options);
				PayloadLog.Write($"Reusable session started or reused for pipe '{options.PipeName}'.");
			}
			else
			{
				runtime.StartOneShot(options);
				PayloadLog.Write($"One-shot session started for pipe '{options.PipeName}'.");
			}

			return 0;
		}
		catch (Exception ex)
		{
			PayloadLog.Write("Payload startup failed.", ex);
			return 1;
		}
	}

	public static void ConfigureRuntimeForTests(IAppDriverPayloadRuntime testRuntime)
	{
		runtime = testRuntime ?? throw new ArgumentNullException(nameof(testRuntime));
	}

	public static void ResetRuntimeForTests()
	{
		runtime = new DefaultAppDriverPayloadRuntime();
		PayloadLog.ResetForTests();
		ReusablePipeSessionRegistry.ClearForTests();
		AppHooks.ResetForTests();
	}
}
