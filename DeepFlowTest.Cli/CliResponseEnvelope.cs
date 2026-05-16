namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Diagnostics;

public sealed class CliResponseEnvelope
{
	public bool Ok { get; set; }

	public string Command { get; set; } = string.Empty;

	public object? Data { get; set; }

	public CliError? Error { get; set; }

	public Dictionary<string, object?> Diagnostics { get; set; } = [];

	public long DurationMs { get; set; }
}

public sealed class CliError
{
	public string Code { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;

	public object? Details { get; set; }
}

public static class CliResponseFactory
{
	public static CliResponseEnvelope Success(
		string command,
		object? data,
		Stopwatch stopwatch,
		Dictionary<string, object?>? diagnostics = null) =>
		new()
		{
			Ok = true,
			Command = command,
			Data = data,
			Diagnostics = diagnostics ?? [],
			DurationMs = stopwatch.ElapsedMilliseconds,
		};

	public static CliResponseEnvelope Error(
		string command,
		string errorCode,
		string message,
		Stopwatch stopwatch,
		object? details = null,
		Dictionary<string, object?>? diagnostics = null) =>
		new()
		{
			Ok = false,
			Command = command,
			Error = new CliError
			{
				Code = errorCode,
				Message = message,
				Details = details,
			},
			Diagnostics = diagnostics ?? [],
			DurationMs = stopwatch.ElapsedMilliseconds,
		};
}
