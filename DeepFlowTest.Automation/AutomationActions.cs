namespace DeepFlowTest.Automation;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public abstract record AutomationAction;

public sealed record ClickAction(MouseButtonKind Button, int Count, bool UseDoubleClickRoutedEvent = false) : AutomationAction;

public sealed record MouseWheelAction(int Delta) : AutomationAction;

public sealed record DragAction(
	int DurationMs = 500,
	int HoldMs = 75,
	int StepIntervalMs = 16,
	int PostDropWaitMs = 100,
	double SourceAnchorX = 0.5,
	double SourceAnchorY = 0.5,
	double DestinationAnchorX = 0.5,
	double DestinationAnchorY = 0.5,
	bool UseInjectedEvents = true,
	bool EnsureForeground = false,
	bool ValidateSameProcess = true) : AutomationAction;

public sealed record FocusAction : AutomationAction;

public sealed record TypeTextAction(string Text, bool ClearFirst) : AutomationAction;

public sealed record KeyPressAction(object? Keys, int DelayMs, bool EnsureForeground, bool ValidateKnownKeys = false) : AutomationAction;

public sealed record SetPropertyAction(string Property, object? Value) : AutomationAction;

public sealed record RoutedEventAction(string EventName) : AutomationAction;

public sealed record KnownOperationAction(string Operation) : AutomationAction;

public sealed record InvokeCodeAction(object? Code, bool AllowUnsafeCode) : AutomationAction;

public enum AutomationActionPolicyClass
{
	Action,
	ArbitraryInvoke,
}

public enum AutomationActionTargetCardinality
{
	Optional,
	One,
	Two,
}

public sealed record AutomationActionDescriptor(
	string Name,
	AutomationActionPolicyClass Policy,
	AutomationActionTargetCardinality TargetCardinality,
	IReadOnlyList<string> AfterProperties);

public static class AutomationActionCatalog
{
	private static readonly HashSet<string> RoutedEvents = new(StringComparer.Ordinal)
	{
		"Click",
		"MouseDoubleClick",
		"Checked",
		"Unchecked",
		"Expanded",
		"Collapsed",
	};

	private static readonly HashSet<string> Operations = new(StringComparer.Ordinal)
	{
		"Focus",
		"AcceptDialog",
		"CancelDialog",
		"BringIntoView",
		"Select",
		"Expand",
		"Collapse",
		"Check",
		"Uncheck",
	};

	public static bool IsKnownRoutedEvent(string? eventName) =>
		!string.IsNullOrWhiteSpace(eventName) && RoutedEvents.Contains(eventName);

	public static bool IsKnownOperation(string? operation) =>
		!string.IsNullOrWhiteSpace(operation) && Operations.Contains(operation);
}

public static class AutomationActionRegistry
{
	private static readonly IReadOnlyDictionary<Type, IActionHandler> Handlers = CreateHandlers();

	public static AutomationActionDescriptor Describe(AutomationAction action)
	{
		ArgumentNullException.ThrowIfNull(action);
		var descriptor = GetHandler(action).Descriptor;
		return action is SetPropertyAction set && !string.IsNullOrWhiteSpace(set.Property)
			? descriptor with { AfterProperties = [set.Property] }
			: descriptor;
	}

	public static void Validate(AutomationAction action)
	{
		ArgumentNullException.ThrowIfNull(action);
		GetHandler(action).Validate(action);
	}

	public static IpcCommand CreateCommand(
		AutomationAction action,
		string? targetId,
		string? destinationTargetId,
		int timeoutMs)
	{
		ArgumentNullException.ThrowIfNull(action);
		var handler = GetHandler(action);
		handler.Validate(action);
		return handler.CreateCommand(action, targetId, destinationTargetId, timeoutMs);
	}

	public static void ValidateRegistrations() => _ = Handlers.Count;

	private static IActionHandler GetHandler(AutomationAction action) =>
		Handlers.TryGetValue(action.GetType(), out var handler)
			? handler
			: throw new InvalidOperationException($"No automation action handler is registered for '{action.GetType().FullName}'.");

	private static IReadOnlyDictionary<Type, IActionHandler> CreateHandlers()
	{
		IActionHandler[] handlers =
		[
			new ClickActionHandler(),
			new MouseWheelActionHandler(),
			new DragActionHandler(),
			new FocusActionHandler(),
			new TypeTextActionHandler(),
			new KeyPressActionHandler(),
			new SetPropertyActionHandler(),
			new RoutedEventActionHandler(),
			new KnownOperationActionHandler(),
			new InvokeCodeActionHandler(),
		];

		var duplicate = handlers.GroupBy(handler => handler.ActionType).FirstOrDefault(group => group.Count() != 1);
		if (duplicate is not null)
			throw new InvalidOperationException($"Automation action '{duplicate.Key.FullName}' has {duplicate.Count()} handlers; exactly one is required.");

		var registered = handlers.Select(handler => handler.ActionType).ToHashSet();
		var concreteActions = typeof(AutomationAction).Assembly.GetTypes()
			.Where(type => !type.IsAbstract && typeof(AutomationAction).IsAssignableFrom(type))
			.ToArray();
		var missing = concreteActions.Where(type => !registered.Contains(type)).ToArray();
		if (missing.Length > 0)
			throw new InvalidOperationException($"Automation actions without handlers: {string.Join(", ", missing.Select(type => type.FullName))}.");

		return handlers.ToDictionary(handler => handler.ActionType);
	}

	private interface IActionHandler
	{
		Type ActionType { get; }

		AutomationActionDescriptor Descriptor { get; }

		void Validate(AutomationAction action);

		IpcCommand CreateCommand(AutomationAction action, string? targetId, string? destinationTargetId, int timeoutMs);
	}

	private abstract class ActionHandler<TAction> : IActionHandler where TAction : AutomationAction
	{
		public Type ActionType => typeof(TAction);

		public abstract AutomationActionDescriptor Descriptor { get; }

		public void Validate(AutomationAction action) => Validate((TAction)action);

		public IpcCommand CreateCommand(AutomationAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			CreateCommand((TAction)action, targetId, destinationTargetId, timeoutMs);

		protected virtual void Validate(TAction action)
		{
		}

		protected abstract IpcCommand CreateCommand(TAction action, string? targetId, string? destinationTargetId, int timeoutMs);

		protected static string RequireTarget(string? targetId) =>
			string.IsNullOrWhiteSpace(targetId)
				? throw new AutomationException(AutomationErrorCodes.InvalidArguments, "The action requires an element target.")
				: targetId;
	}

	private sealed class ClickActionHandler : ActionHandler<ClickAction>
	{
		public override AutomationActionDescriptor Descriptor { get; } = new("click", AutomationActionPolicyClass.Action, AutomationActionTargetCardinality.One, []);

		protected override void Validate(ClickAction action)
		{
			if (action.Count <= 0)
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Click count must be greater than zero.");
		}

		protected override IpcCommand CreateCommand(ClickAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			action.UseDoubleClickRoutedEvent
				? new KnownRoutedEventCommandRequest { TargetId = RequireTarget(targetId), EventName = "MouseDoubleClick", TimeoutMs = timeoutMs }
				: new ClickCommandRequest { TargetId = RequireTarget(targetId), MouseButton = action.Button, ClickCount = action.Count, TimeoutMs = timeoutMs };
	}

	private sealed class MouseWheelActionHandler : ActionHandler<MouseWheelAction>
	{
		public override AutomationActionDescriptor Descriptor { get; } = new("wheel", AutomationActionPolicyClass.Action, AutomationActionTargetCardinality.One, []);

		protected override void Validate(MouseWheelAction action)
		{
			if (action.Delta == 0)
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Mouse wheel delta must not be zero.");
		}

		protected override IpcCommand CreateCommand(MouseWheelAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			new MouseWheelCommandRequest { TargetId = RequireTarget(targetId), Delta = action.Delta, TimeoutMs = timeoutMs };
	}

	private sealed class DragActionHandler : ActionHandler<DragAction>
	{
		public override AutomationActionDescriptor Descriptor { get; } = new("drag", AutomationActionPolicyClass.Action, AutomationActionTargetCardinality.Two, []);

		protected override IpcCommand CreateCommand(DragAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			new DragAndDropCommandRequest
			{
				TargetId = RequireTarget(targetId),
				DestinationTargetId = RequireTarget(destinationTargetId),
				DurationMs = action.DurationMs,
				HoldMs = action.HoldMs,
				StepIntervalMs = action.StepIntervalMs,
				PostDropWaitMs = action.PostDropWaitMs,
				SourceAnchorX = action.SourceAnchorX,
				SourceAnchorY = action.SourceAnchorY,
				DestinationAnchorX = action.DestinationAnchorX,
				DestinationAnchorY = action.DestinationAnchorY,
				UseInjectedEvents = action.UseInjectedEvents,
				EnsureForeground = action.EnsureForeground,
				ValidateSameProcess = action.ValidateSameProcess,
				TimeoutMs = timeoutMs,
			};
	}

	private sealed class FocusActionHandler : ActionHandler<FocusAction>
	{
		public override AutomationActionDescriptor Descriptor { get; } = new(
			"focus",
			AutomationActionPolicyClass.Action,
			AutomationActionTargetCardinality.One,
			[KnownProperties.IsFocused, KnownProperties.IsKeyboardFocused, KnownProperties.IsKeyboardFocusWithin]);

		protected override IpcCommand CreateCommand(FocusAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			new FocusCommandRequest { TargetId = RequireTarget(targetId), TimeoutMs = timeoutMs };
	}

	private sealed class TypeTextActionHandler : ActionHandler<TypeTextAction>
	{
		public override AutomationActionDescriptor Descriptor { get; } = new(
			"type",
			AutomationActionPolicyClass.Action,
			AutomationActionTargetCardinality.Optional,
			[KnownProperties.Text, KnownProperties.Content]);

		protected override void Validate(TypeTextAction action)
		{
			if (action.Text is null)
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Text is required.");
		}

		protected override IpcCommand CreateCommand(TypeTextAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			new TypeTextCommandRequest { TargetId = targetId, Text = action.Text, ClearFirst = action.ClearFirst, TimeoutMs = timeoutMs };
	}

	private sealed class KeyPressActionHandler : ActionHandler<KeyPressAction>
	{
		private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
		{
			"Enter", "Return", "Tab", "Escape", "Esc", "Space", "Backspace", "Delete", "Del",
			"Insert", "Ins", "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right",
			"Ctrl", "Control", "Shift", "Alt", "A", "C", "V", "X", "Y", "Z",
			"F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
		};

		public override AutomationActionDescriptor Descriptor { get; } = new(
			"key",
			AutomationActionPolicyClass.Action,
			AutomationActionTargetCardinality.Optional,
			[KnownProperties.Text, KnownProperties.Content, KnownProperties.IsKeyboardFocused, KnownProperties.IsKeyboardFocusWithin]);

		protected override void Validate(KeyPressAction action)
		{
			var keys = Convert.ToString(action.Keys);
			if (string.IsNullOrWhiteSpace(keys))
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Keys are required.");

			if (!action.ValidateKnownKeys)
				return;

			foreach (var token in keys.Split(['+', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
					continue;
				if (!KnownKeys.Contains(token))
					throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Unknown key name '{token}'.");
			}
		}

		protected override IpcCommand CreateCommand(KeyPressAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			new KeyPressCommandRequest
			{
				TargetId = targetId,
				Keys = action.Keys,
				DelayMs = action.DelayMs,
				EnsureForeground = action.EnsureForeground,
				TimeoutMs = timeoutMs,
			};
	}

	private sealed class SetPropertyActionHandler : ActionHandler<SetPropertyAction>
	{
		public override AutomationActionDescriptor Descriptor { get; } = new(
			"set", AutomationActionPolicyClass.Action, AutomationActionTargetCardinality.One, []);

		protected override void Validate(SetPropertyAction action)
		{
			if (string.IsNullOrWhiteSpace(action.Property))
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Property is required.");
		}

		protected override IpcCommand CreateCommand(SetPropertyAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			new SetPropertyCommandRequest { TargetId = RequireTarget(targetId), PropertyName = action.Property, PropertyValue = action.Value, TimeoutMs = timeoutMs };
	}

	private sealed class RoutedEventActionHandler : ActionHandler<RoutedEventAction>
	{
		public override AutomationActionDescriptor Descriptor { get; } = new("raise", AutomationActionPolicyClass.Action, AutomationActionTargetCardinality.One, []);

		protected override void Validate(RoutedEventAction action)
		{
			if (!AutomationActionCatalog.IsKnownRoutedEvent(action.EventName))
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Routed event '{action.EventName}' is not allow-listed.");
		}

		protected override IpcCommand CreateCommand(RoutedEventAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			new KnownRoutedEventCommandRequest { TargetId = RequireTarget(targetId), EventName = action.EventName, TimeoutMs = timeoutMs };
	}

	private sealed class KnownOperationActionHandler : ActionHandler<KnownOperationAction>
	{
		public override AutomationActionDescriptor Descriptor { get; } = new("invoke", AutomationActionPolicyClass.Action, AutomationActionTargetCardinality.One, []);

		protected override void Validate(KnownOperationAction action)
		{
			if (!AutomationActionCatalog.IsKnownOperation(action.Operation))
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Known operation '{action.Operation}' is not allow-listed.");
		}

		protected override IpcCommand CreateCommand(KnownOperationAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			new KnownOperationCommandRequest { TargetId = RequireTarget(targetId), Operation = action.Operation, TimeoutMs = timeoutMs };
	}

	private sealed class InvokeCodeActionHandler : ActionHandler<InvokeCodeAction>
	{
		public override AutomationActionDescriptor Descriptor { get; } = new("invoke", AutomationActionPolicyClass.ArbitraryInvoke, AutomationActionTargetCardinality.One, []);

		protected override IpcCommand CreateCommand(InvokeCodeAction action, string? targetId, string? destinationTargetId, int timeoutMs) =>
			new InvokeCommandRequest { TargetId = RequireTarget(targetId), Code = action.Code, AllowUnsafeCode = action.AllowUnsafeCode, TimeoutMs = timeoutMs };
	}
}
