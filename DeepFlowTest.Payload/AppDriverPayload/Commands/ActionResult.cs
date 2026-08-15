namespace DeepFlowTest.AppDriverPayload.Commands;

internal readonly struct ActionResult
{
	private ActionResult(bool success, string? error, object? value = null, string? errorCode = null, bool formatErrorContext = true)
	{
		Success = success;
		Error = error;
		Value = value;
		ErrorCode = errorCode;
		FormatErrorContext = formatErrorContext;
	}

	public bool Success { get; }

	public string? Error { get; }

	public object? Value { get; }

	public string? ErrorCode { get; }

	public bool FormatErrorContext { get; }

	public static ActionResult Ok(object? value = null) => new(true, null, value);

	public static ActionResult Unsupported(string error) => new(false, error);

	public static ActionResult Failure(string error, string errorCode, bool formatErrorContext = true) =>
		new(false, error, errorCode: errorCode, formatErrorContext: formatErrorContext);
}
