namespace DeepFlowTest.Cli;

using System;
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
		bool requireElementTarget)
	{
		_ = session ?? throw new ArgumentNullException(nameof(session));
		_ = commonOptions ?? throw new ArgumentNullException(nameof(commonOptions));
		_ = defaults ?? throw new ArgumentNullException(nameof(defaults));
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		_ = createCommand ?? throw new ArgumentNullException(nameof(createCommand));

		VisualTreeSnapshot? beforeSnapshot = null;
		ElementResolution? resolution = null;
		if (requireElementTarget || !selector.IsEmpty)
		{
			beforeSnapshot = ReadSnapshot(session, commonOptions, defaults);
			resolution = resolver.Resolve(beforeSnapshot, selector);
		}

		var payload = session.Send<object>(createCommand(resolution?.TargetId), commonOptions.TimeoutMs);
		EnsurePayloadSucceeded(payload);

		return new ActionCommandResult
		{
			Action = actionName,
			Target = resolution?.Summary,
			Payload = payload,
			After = CreateAfterSnapshot(session, commonOptions, defaults, resolution?.TargetId),
		};
	}

	private static object? CreateAfterSnapshot(ICliAppSession session, CliCommonOptions commonOptions, CliDefaults defaults, string? targetId)
	{
		if (string.Equals(commonOptions.After, "none", StringComparison.Ordinal))
			return null;

		var snapshot = ReadSnapshot(session, commonOptions, defaults);
		if (string.Equals(commonOptions.After, "tree", StringComparison.Ordinal))
		{
			return new TreeSnapshotService().Shape(snapshot, new TreeSnapshotOptions
			{
				Shape = defaults.TreeShape,
				Limit = defaults.TreeLimit,
				Properties = defaults.PropertyNames,
				UseShortIds = commonOptions.UseShortIds,
			});
		}

		if (!string.IsNullOrWhiteSpace(targetId))
		{
			return new NodeSnapshotService().GetNode(snapshot, new NodeSnapshotOptions
			{
				TargetId = targetId!,
				Properties = defaults.PropertyNames,
				UseShortIds = commonOptions.UseShortIds,
			});
		}

		return null;
	}

	private static VisualTreeSnapshot ReadSnapshot(ICliAppSession session, CliCommonOptions commonOptions, CliDefaults defaults)
	{
		var response = session.Send<object>(
			new GetVisualTreeCommandRequest
			{
				PropNames = defaults.PropertyNames,
				AsSnapshot = true,
				IncludeHidden = true,
				MaxNodeCount = defaults.TreeLimit,
				TimeoutMs = commonOptions.TimeoutMs,
			},
			commonOptions.TimeoutMs);
		return new VisualTreeResponseReader().Read(response, defaults.PropertyNames);
	}

	private static void EnsurePayloadSucceeded(object payload)
	{
		if (payload is not StandardIpcResponse standard || standard.Success != false)
			return;

		throw new CliException(MapProtocolError(standard.ErrorCode), standard.Error ?? "Payload action failed.", standard);
	}

	private static string MapProtocolError(string? errorCode)
	{
		return errorCode switch
		{
			ProtocolConstants.ErrorCodes.StaleTarget => CliErrorCodes.StaleTarget,
			ProtocolConstants.ErrorCodes.TargetExited => CliErrorCodes.TargetExited,
			ProtocolConstants.ErrorCodes.UnsupportedTarget => CliErrorCodes.UnsupportedTarget,
			ProtocolConstants.ErrorCodes.CommandTimeout => CliErrorCodes.CommandTimeout,
			ProtocolConstants.ErrorCodes.UnsupportedCommand => CliErrorCodes.UnsupportedTarget,
			_ => CliErrorCodes.ProtocolError,
		};
	}
}

public sealed class ActionCommandResult
{
	public string Action { get; set; } = string.Empty;

	public TreeNodeData? Target { get; set; }

	public object? Payload { get; set; }

	public object? After { get; set; }
}
