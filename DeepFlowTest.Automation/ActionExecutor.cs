namespace DeepFlowTest.Automation;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class ActionExecutor
{
	private readonly ElementResolver resolver;

	public ActionExecutor(ElementResolver? resolver = null)
	{
		this.resolver = resolver ?? new ElementResolver();
	}

	public ActionExecutionResult Execute(
		string actionName,
		IAutomationSession session,
		AutomationExecutionOptions options,
		ElementSelector selector,
		Func<string?, IpcCommand> createCommand,
		bool requireElementTarget,
		IReadOnlyList<string>? afterProperties = null)
	{
		_ = session ?? throw new ArgumentNullException(nameof(session));
		_ = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		_ = createCommand ?? throw new ArgumentNullException(nameof(createCommand));

		ElementResolution? resolution = null;
		if (!string.IsNullOrWhiteSpace(selector.TargetId) && LooksLikeFullTargetId(selector.TargetId!))
		{
			resolution = new ElementResolution
			{
				TargetId = selector.TargetId!,
				Summary = new TreeNodeData
				{
					TargetId = selector.TargetId!,
					ShortId = new TargetIdService().GetShortId(selector.TargetId!),
				},
			};
		}
		else if (requireElementTarget || !selector.IsEmpty)
		{
			var beforeSnapshot = ReadSnapshot(session, options);
			resolution = resolver.Resolve(beforeSnapshot, selector);
		}

		var payload = session.Send<object>(createCommand(resolution?.TargetId), options.TimeoutMs);
		EnsurePayloadSucceeded(payload);

		return new ActionExecutionResult
		{
			Action = actionName,
			Target = resolution?.Summary,
			Payload = payload,
			After = CreateAfterSnapshot(session, options, resolution?.TargetId, afterProperties),
		};
	}

	public TwoTargetActionExecutionResult ExecuteTwoTarget(
		string actionName,
		IAutomationSession session,
		AutomationExecutionOptions options,
		ElementSelector sourceSelector,
		ElementSelector destinationSelector,
		Func<string, string, IpcCommand> createCommand,
		IReadOnlyList<string>? afterProperties = null)
	{
		_ = session ?? throw new ArgumentNullException(nameof(session));
		_ = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
		_ = sourceSelector ?? throw new ArgumentNullException(nameof(sourceSelector));
		_ = destinationSelector ?? throw new ArgumentNullException(nameof(destinationSelector));
		_ = createCommand ?? throw new ArgumentNullException(nameof(createCommand));

		VisualTreeSnapshot? beforeSnapshot = null;
		VisualTreeSnapshot GetBeforeSnapshot()
		{
			beforeSnapshot ??= ReadSnapshot(session, options);
			return beforeSnapshot;
		}

		var source = ResolveTarget(sourceSelector, GetBeforeSnapshot);
		var destination = ResolveTarget(destinationSelector, GetBeforeSnapshot);
		var payload = session.Send<object>(createCommand(source.TargetId, destination.TargetId), options.TimeoutMs);
		EnsurePayloadSucceeded(payload);

		return new TwoTargetActionExecutionResult
		{
			Action = actionName,
			Source = source.Summary,
			Destination = destination.Summary,
			Payload = payload,
			After = CreateAfterSnapshot(session, options, destination.TargetId, afterProperties),
		};
	}

	private static object? CreateAfterSnapshot(
		IAutomationSession session,
		AutomationExecutionOptions options,
		string? targetId,
		IReadOnlyList<string>? afterProperties)
	{
		if (options.After == ObservationMode.None)
			return null;

		var properties = MergeProperties(options.Properties, afterProperties);
		var snapshot = ReadSnapshot(session, options, properties);
		if (options.After == ObservationMode.Tree)
		{
			return new TreeSnapshotService().Shape(snapshot, new TreeSnapshotOptions
			{
				Shape = options.TreeShape,
				Limit = options.TreeLimit,
				Properties = properties,
				UseShortIds = options.UseShortIds,
			});
		}

		if (!string.IsNullOrWhiteSpace(targetId))
		{
			return new NodeSnapshotService().GetNode(snapshot, new NodeSnapshotOptions
			{
				TargetId = targetId!,
				Properties = properties,
				UseShortIds = options.UseShortIds,
			});
		}

		return null;
	}

	private static VisualTreeSnapshot ReadSnapshot(IAutomationSession session, AutomationExecutionOptions options)
	{
		return ReadSnapshot(session, options, options.Properties);
	}

	private static VisualTreeSnapshot ReadSnapshot(
		IAutomationSession session,
		AutomationExecutionOptions options,
		IReadOnlyList<string> properties)
	{
		var response = session.Send<object>(
			new GetVisualTreeCommandRequest
			{
				PropNames = properties,
				AsSnapshot = true,
				IncludeHidden = true,
				MaxNodeCount = options.TreeLimit,
				TimeoutMs = options.TimeoutMs,
			},
			options.TimeoutMs);
		return new VisualTreeResponseReader().Read(response, properties);
	}

	private static IReadOnlyList<string> MergeProperties(
		IEnumerable<string> defaults,
		IEnumerable<string>? extras)
	{
		List<string> result = [];
		foreach (var property in defaults.Concat(extras ?? []))
			if (!string.IsNullOrWhiteSpace(property) && !result.Contains(property, StringComparer.Ordinal))
				result.Add(property);

		return result;
	}

	private static void EnsurePayloadSucceeded(object payload)
	{
		if (payload is not StandardIpcResponse standard || standard.Success != false)
			return;

		throw new AutomationException(ProtocolErrorMapper.Map(standard.ErrorCode), standard.Error ?? "Payload action failed.", standard);
	}

	private static bool LooksLikeFullTargetId(string targetId) =>
		targetId.Length > 8 && targetId.Contains('-', StringComparison.Ordinal);

	private ElementResolution ResolveTarget(ElementSelector selector, Func<VisualTreeSnapshot> readSnapshot)
	{
		if (!string.IsNullOrWhiteSpace(selector.TargetId) && LooksLikeFullTargetId(selector.TargetId!))
		{
			return new ElementResolution
			{
				TargetId = selector.TargetId!,
				Summary = new TreeNodeData
				{
					TargetId = selector.TargetId!,
					ShortId = new TargetIdService().GetShortId(selector.TargetId!),
				},
			};
		}

		return resolver.Resolve(readSnapshot(), selector);
	}

}

public sealed class ActionExecutionResult
{
	public string Action { get; set; } = string.Empty;

	public TreeNodeData? Target { get; set; }

	public object? Payload { get; set; }

	public object? After { get; set; }
}

public sealed class TwoTargetActionExecutionResult
{
	public string Action { get; set; } = string.Empty;

	public TreeNodeData? Source { get; set; }

	public TreeNodeData? Destination { get; set; }

	public object? Payload { get; set; }

	public object? After { get; set; }
}
