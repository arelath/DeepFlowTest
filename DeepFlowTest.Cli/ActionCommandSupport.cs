namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class ActionCommandSupport
{
	private readonly ElementResolver resolver;

	public ActionCommandSupport(ElementResolver? resolver = null)
	{
		this.resolver = resolver ?? new ElementResolver();
	}

	public ActionCommandResult Execute(
		string actionName,
		ICliAppSession session,
		CliCommonOptions commonOptions,
		CliDefaults defaults,
		ElementSelector selector,
		Func<string?, IpcCommand> createCommand,
		bool requireElementTarget,
		IReadOnlyList<string>? afterProperties = null)
	{
		_ = session ?? throw new ArgumentNullException(nameof(session));
		_ = commonOptions ?? throw new ArgumentNullException(nameof(commonOptions));
		_ = defaults ?? throw new ArgumentNullException(nameof(defaults));
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
					ShortId = new CliTargetIdService().GetShortId(selector.TargetId!),
				},
			};
		}
		else if (requireElementTarget || !selector.IsEmpty)
		{
			var beforeSnapshot = ReadSnapshot(session, commonOptions, defaults);
			resolution = resolver.Resolve(beforeSnapshot, selector);
		}

		var payload = session.Send<object>(createCommand(resolution?.TargetId), commonOptions.TimeoutMs);
		EnsurePayloadSucceeded(payload);

		return new ActionCommandResult
		{
			Action = actionName,
			Target = resolution?.Summary,
			Payload = payload,
			After = CreateAfterSnapshot(session, commonOptions, defaults, resolution?.TargetId, afterProperties),
		};
	}

	private static object? CreateAfterSnapshot(
		ICliAppSession session,
		CliCommonOptions commonOptions,
		CliDefaults defaults,
		string? targetId,
		IReadOnlyList<string>? afterProperties)
	{
		if (string.Equals(commonOptions.After, "none", StringComparison.Ordinal))
			return null;

		var properties = MergeProperties(defaults.PropertyNames, afterProperties);
		var snapshot = ReadSnapshot(session, commonOptions, defaults, properties);
		if (string.Equals(commonOptions.After, "tree", StringComparison.Ordinal))
		{
			return new TreeSnapshotService().Shape(snapshot, new TreeSnapshotOptions
			{
				Shape = defaults.Commands.Tree.Shape,
				Limit = defaults.TreeLimit,
				Properties = properties,
				UseShortIds = commonOptions.UseShortIds,
			});
		}

		if (!string.IsNullOrWhiteSpace(targetId))
		{
			return new NodeSnapshotService().GetNode(snapshot, new NodeSnapshotOptions
			{
				TargetId = targetId!,
				Properties = properties,
				UseShortIds = commonOptions.UseShortIds,
			});
		}

		return null;
	}

	private static VisualTreeSnapshot ReadSnapshot(ICliAppSession session, CliCommonOptions commonOptions, CliDefaults defaults)
	{
		return ReadSnapshot(session, commonOptions, defaults, defaults.PropertyNames);
	}

	private static VisualTreeSnapshot ReadSnapshot(
		ICliAppSession session,
		CliCommonOptions commonOptions,
		CliDefaults defaults,
		IReadOnlyList<string> properties)
	{
		var response = session.Send<object>(
			new GetVisualTreeCommandRequest
			{
				PropNames = properties,
				AsSnapshot = true,
				IncludeHidden = true,
				MaxNodeCount = defaults.TreeLimit,
				TimeoutMs = commonOptions.TimeoutMs,
			},
			commonOptions.TimeoutMs);
		return new VisualTreeResponseReader().Read(response, properties);
	}

	private static IReadOnlyList<string> MergeProperties(
		IEnumerable<string> defaults,
		IEnumerable<string>? extras)
	{
		var result = new List<string>();
		foreach (var property in defaults.Concat(extras ?? Array.Empty<string>()))
			if (!string.IsNullOrWhiteSpace(property) && !result.Contains(property, StringComparer.Ordinal))
				result.Add(property);

		return result;
	}

	private static void EnsurePayloadSucceeded(object payload)
	{
		if (payload is not StandardIpcResponse standard || standard.Success != false)
			return;

		throw new CliException(ProtocolErrorMapper.Map(standard.ErrorCode), standard.Error ?? "Payload action failed.", standard);
	}

	private static bool LooksLikeFullTargetId(string targetId) =>
		targetId.Length > 8 && targetId.Contains('-', StringComparison.Ordinal);

}

public sealed class ActionCommandResult
{
	public string Action { get; set; } = string.Empty;

	public TreeNodeData? Target { get; set; }

	public object? Payload { get; set; }

	public object? After { get; set; }
}
