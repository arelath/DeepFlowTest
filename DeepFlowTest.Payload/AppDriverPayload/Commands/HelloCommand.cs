namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Reflection;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal static class HelloCommand
{
	public static object Process(
		HelloCommandRequest request,
		AppDriverPayloadStartupOptions options,
		ReusablePipeSession? reusableSession,
		string? connectionId = null)
	{
		if (!string.Equals(request.ProtocolVersion, ProtocolConstants.ProtocolVersion, StringComparison.Ordinal))
		{
			return StandardIpcResponse.FromError(
				$"Protocol version '{request.ProtocolVersion}' is not supported.",
				ProtocolConstants.ErrorCodes.UnsupportedProtocol,
				PayloadLog.CurrentCorrelationId);
		}

		return new HelloCommandResponse
		{
			ProtocolVersion = ProtocolConstants.ProtocolVersion,
			PayloadVersion = typeof(HelloCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
				?? typeof(HelloCommand).Assembly.GetName().Version?.ToString(),
			PipeName = options.PipeName,
			IsReusable = reusableSession is not null || options.Mode == PayloadStartupModes.ReusableCli,
			ConnectionId = connectionId,
			ControlConnectionMode = reusableSession is not null || options.Mode == PayloadStartupModes.ReusableCli
				? ProtocolConstants.ControlConnectionModes.PersistentSerialized
				: ProtocolConstants.ControlConnectionModes.OneShot,
			ProcessId = PayloadEnvironment.ProcessId,
			ProcessArchitecture = PayloadEnvironment.ProcessArchitecture,
			FrameworkFamily = PayloadEnvironment.FrameworkFamily,
			Timestamp = DateTimeOffset.UtcNow,
		};
	}

}
