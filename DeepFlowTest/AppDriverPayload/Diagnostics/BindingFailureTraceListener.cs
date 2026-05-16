namespace DeepFlowTest.AppDriverPayload.Diagnostics;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using DeepFlowTest.Contracts;

internal sealed class BindingFailureTraceListener : TraceListener
{
	private readonly BindingFailureCaptureService captureService;

	public BindingFailureTraceListener(BindingFailureCaptureService captureService)
	{
		this.captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
	}

	public override void Write(string? message)
	{
		Capture(InferSeverity(message), message);
	}

	public override void WriteLine(string? message)
	{
		Capture(InferSeverity(message), message);
	}

	public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
	{
		Capture(MapSeverity(eventType), message, source, id);
	}

	public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
	{
		var message = args is { Length: > 0 }
			? string.Format(CultureInfo.InvariantCulture, format ?? string.Empty, args)
			: format;
		Capture(MapSeverity(eventType), message, source, id);
	}

	public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, object? data)
	{
		Capture(MapSeverity(eventType), Convert.ToString(data, CultureInfo.InvariantCulture), source, id);
	}

	public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, params object?[]? data)
	{
		var message = data is null
			? string.Empty
			: string.Join(" ", data.Select(static item => Convert.ToString(item, CultureInfo.InvariantCulture)));
		Capture(MapSeverity(eventType), message, source, id);
	}

	private void Capture(BindingFailureSeverity severity, string? message, string? source = null, int? eventId = null)
	{
		captureService.Record(severity, message, source, eventId);
	}

	private static BindingFailureSeverity MapSeverity(TraceEventType eventType) =>
		eventType switch
		{
			TraceEventType.Critical or TraceEventType.Error => BindingFailureSeverity.Error,
			TraceEventType.Warning => BindingFailureSeverity.Warning,
			TraceEventType.Information => BindingFailureSeverity.Information,
			_ => BindingFailureSeverity.Verbose,
		};

	private static BindingFailureSeverity InferSeverity(string? message)
	{
		if (message?.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
			return BindingFailureSeverity.Error;
		if (message?.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0)
			return BindingFailureSeverity.Warning;

		return BindingFailureSeverity.Warning;
	}
}
