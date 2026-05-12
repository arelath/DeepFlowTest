namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Reflection;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal static class HelloCommand
{
	public static object Process(HelloCommandRequest request, AppDriverPayloadStartupOptions options, ReusablePipeSession? reusableSession)
	{
		if (!string.Equals(request.ProtocolVersion, ProtocolConstants.ProtocolVersion, StringComparison.Ordinal))
		{
			return StandardIpcResponse.FromError(
				$"Protocol version '{request.ProtocolVersion}' is not supported.",
				ProtocolConstants.ErrorCodes.UnsupportedProtocol,
				LogCorrelationId());
		}

		return new HelloCommandResponse
		{
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
			PayloadVersion = typeof(HelloCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
				?? typeof(HelloCommand).Assembly.GetName().Version?.ToString(),
			PipeName = options.PipeName,
			IsReusable = reusableSession is not null || options.Mode == PayloadStartupModes.ReusableCli,
			ProcessId = PayloadEnvironment.ProcessId,
			ProcessArchitecture = PayloadEnvironment.ProcessArchitecture,
			FrameworkFamily = PayloadEnvironment.FrameworkFamily,
			Timestamp = DateTimeOffset.UtcNow,
		};
	}

	private static string LogCorrelationId()
	{
		return System.IO.Path.GetFileNameWithoutExtension(PayloadLog.CurrentLogPath);
	}
}
