namespace DeepFlowTest;

using System;
using DeepFlowTest.Contracts;

internal sealed class DriverCommandClient(
	IUnsafeAppDriverCommandSession session,
	Action? afterSuccessfulCommand = null,
	Action<Exception>? onFailure = null)
{
	private readonly IUnsafeAppDriverCommandSession session = session ?? throw new ArgumentNullException(nameof(session));
	private readonly Action? afterSuccessfulCommand = afterSuccessfulCommand;
	private readonly Action<Exception>? onFailure = onFailure;

	public TResponse Send<TResponse>(IpcCommand command)
	{
		try
		{
			var response = session.Send<TResponse>(command);
			if (IsFailure(response, out var errorCode, out var errorMessage))
			{
				NotifyFailure(new AppDriverException(
					errorCode ?? ProtocolConstants.ErrorCodes.ProtocolError,
					errorMessage ?? "The driver command failed."));
			}
			else
			{
				afterSuccessfulCommand?.Invoke();
			}

			return response;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			NotifyFailure(ex);
			throw;
		}
	}

	private void NotifyFailure(Exception exception)
	{
		try
		{
			onFailure?.Invoke(exception);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
		}
	}

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
