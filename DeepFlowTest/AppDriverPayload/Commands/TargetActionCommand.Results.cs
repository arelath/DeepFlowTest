namespace DeepFlowTest.AppDriverPayload.Commands;

internal static partial class TargetActionCommand
{
	private readonly struct ActionResult
	{
		private ActionResult(bool success, string? error, object? value = null)
		{
			Success = success;
			Error = error;
			Value = value;
		}

		public bool Success { get; }

		public string? Error { get; }

		public object? Value { get; }

		public static ActionResult Ok(object? value = null) => new(true, null, value);

		public static ActionResult Unsupported(string error) => new(false, error);
	}
}
