namespace DeepFlowTest;

using System;
using DeepFlowTest.Contracts;

internal sealed class DriverCommandClient
{
	private readonly IAppDriverCommandSession session;

	public DriverCommandClient(IAppDriverCommandSession session)
	{
		this.session = session ?? throw new ArgumentNullException(nameof(session));
	}

	public TResponse Send<TResponse>(IpcCommand command) =>
		session.Send<TResponse>(command);

	public static bool IsFailure<TResponse>(TResponse response, string errorCode) =>
		IsFailure(response, out var actualErrorCode, out _) &&
		string.Equals(actualErrorCode, errorCode, StringComparison.Ordinal);

	public static bool IsFailure<TResponse>(TResponse response, out string? errorCode, out string? errorMessage)
	{
		errorCode = null;
		errorMessage = null;
		if (response is null)
			return false;

		var responseType = response.GetType();
		var success = responseType.GetProperty(nameof(StandardIpcResponse.Success))?.GetValue(response);
		if (success is not bool successValue || successValue)
			return false;

		errorCode = responseType.GetProperty(nameof(StandardIpcResponse.ErrorCode))?.GetValue(response)?.ToString();
		errorMessage = responseType.GetProperty(nameof(StandardIpcResponse.Error))?.GetValue(response)?.ToString();
		return true;
	}

	public static void ThrowIfStandardFailure(StandardIpcResponse response, string fallbackMessage)
	{
		if (response.Success != true)
			throw new AppDriverException(response.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, response.Error ?? fallbackMessage);
	}

	public static void ThrowIfFailure<TResponse>(TResponse response, string fallbackMessage)
	{
		if (response is StandardIpcResponse standard && standard.Success == false)
			throw new AppDriverException(standard.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, standard.Error ?? fallbackMessage);
		if (IsFailure(response, out var errorCode, out var errorMessage))
			throw new AppDriverException(errorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, errorMessage ?? fallbackMessage);
	}
}
