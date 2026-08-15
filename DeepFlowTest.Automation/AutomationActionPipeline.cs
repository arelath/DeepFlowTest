namespace DeepFlowTest.Automation;

using System;
using System.Linq;

public sealed record AutomationActionRequest(
	AutomationAction Action,
	ElementSelector Target,
	ElementSelector? Destination = null);

public sealed record AutomationActionRetry(ElementSelector Target, ElementSelector? Destination = null);

public sealed class AutomationActionPipelineHooks
{
	public Action<AutomationActionDescriptor>? DemandPolicy { get; init; }

	public Action? InvalidateCache { get; init; }

	public Func<AutomationException, int, AutomationActionRetry?>? RepairStaleTarget { get; init; }

	public Action<AutomationActionPipelineResult>? Verify { get; init; }

	public Action<AutomationActionPipelineResult>? Observe { get; init; }
}

public sealed class AutomationActionPipelineResult
{
	public ActionExecutionResult? SingleTarget { get; init; }

	public TwoTargetActionExecutionResult? TwoTarget { get; init; }

	public string Action => SingleTarget?.Action ?? TwoTarget?.Action ?? string.Empty;
}

public sealed class AutomationActionPipeline
{
	private readonly ActionExecutor executor;

	public AutomationActionPipeline(ActionExecutor? executor = null)
	{
		this.executor = executor ?? new ActionExecutor();
	}

	public AutomationActionDescriptor Prepare(AutomationAction action, AutomationActionPipelineHooks? hooks = null)
	{
		ArgumentNullException.ThrowIfNull(action);
		var descriptor = AutomationActionRegistry.Describe(action);
		hooks?.DemandPolicy?.Invoke(descriptor);
		AutomationActionRegistry.Validate(action);
		return descriptor;
	}

	public AutomationActionPipelineResult Execute(
		IAutomationSession session,
		AutomationExecutionOptions options,
		AutomationActionRequest request,
		AutomationActionPipelineHooks? hooks = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(request.Action);
		ArgumentNullException.ThrowIfNull(request.Target);

		var descriptor = Prepare(request.Action, hooks);
		return ExecutePrepared(session, options, request, descriptor, hooks);
	}

	public AutomationActionPipelineResult ExecutePrepared(
		IAutomationSession session,
		AutomationExecutionOptions options,
		AutomationActionRequest request,
		AutomationActionDescriptor descriptor,
		AutomationActionPipelineHooks? hooks = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(request.Action);
		ArgumentNullException.ThrowIfNull(request.Target);
		ArgumentNullException.ThrowIfNull(descriptor);
		var expectedDescriptor = AutomationActionRegistry.Describe(request.Action);
		if (descriptor.Name != expectedDescriptor.Name
			|| descriptor.Policy != expectedDescriptor.Policy
			|| descriptor.TargetCardinality != expectedDescriptor.TargetCardinality
			|| !descriptor.AfterProperties.SequenceEqual(expectedDescriptor.AfterProperties, StringComparer.Ordinal))
			throw new InvalidOperationException("The prepared action descriptor does not match the execution request.");

		var current = request;
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				var result = ExecuteOnce(session, options, current, descriptor);
				hooks?.InvalidateCache?.Invoke();
				hooks?.Verify?.Invoke(result);
				hooks?.Observe?.Invoke(result);
				return result;
			}
			catch (AutomationException ex) when (
				ex.ErrorCode == AutomationErrorCodes.StaleTarget
				&& hooks?.RepairStaleTarget is not null
				&& attempt == 0)
			{
				hooks.InvalidateCache?.Invoke();
				var retry = hooks.RepairStaleTarget(ex, attempt + 1);
				if (retry is null)
					throw;
				current = request with { Target = retry.Target, Destination = retry.Destination ?? request.Destination };
			}
		}
	}

	private AutomationActionPipelineResult ExecuteOnce(
		IAutomationSession session,
		AutomationExecutionOptions options,
		AutomationActionRequest request,
		AutomationActionDescriptor descriptor)
	{
		if (descriptor.TargetCardinality == AutomationActionTargetCardinality.Two)
		{
			if (request.Destination is null)
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Action '{descriptor.Name}' requires a destination target.");

			return new AutomationActionPipelineResult
			{
				TwoTarget = executor.ExecuteTwoTarget(
					descriptor.Name,
					session,
					options,
					request.Target,
					request.Destination,
					(source, destination) => AutomationActionRegistry.CreateCommand(request.Action, source, destination, options.TimeoutMs),
					descriptor.AfterProperties),
			};
		}

		return new AutomationActionPipelineResult
		{
			SingleTarget = executor.Execute(
				descriptor.Name,
				session,
				options,
				request.Target,
				target => AutomationActionRegistry.CreateCommand(request.Action, target, null, options.TimeoutMs),
				descriptor.TargetCardinality == AutomationActionTargetCardinality.One,
				descriptor.AfterProperties),
		};
	}
}
