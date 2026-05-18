namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;

internal abstract class UiTargetAdapterBase : IUiTargetAdapter
{
	public abstract bool CanHandle(object target);

	public virtual ActionResult Click(object target, MouseButtonKind button, int clickCount) =>
		UnsupportedAdapterAction(target, $"{ProtocolValueMapper.FormatMouseButton(button)} click");

	public virtual ActionResult Focus(object target) =>
		UnsupportedAdapterAction(target, "focus");

	public virtual ActionResult TypeText(object target, string text, bool clearFirst) =>
		UnsupportedAdapterAction(target, "text input");

	public virtual ActionResult SendKeys(object target, object? keys, string keyText, int delayMs) =>
		UnsupportedAdapterAction(target, "key input");

	public virtual bool TryEnsureForeground(object target) =>
		Focus(target).Success;

	public virtual PointerTargetResult GetPointerTarget(object target, PointerAnchor anchor) =>
		PointerTargetResult.Unsupported($"Target type '{target.GetType().FullName}' cannot be converted to screen coordinates.");

	public virtual ActionResult SetProperty(object target, string propertyName, object? value) =>
		TrySetClrProperty(target, propertyName, value, out var result)
			? result
			: ActionResult.Unsupported($"Property '{propertyName}' was not found.");

	public virtual ActionResult RaiseKnownRoutedEvent(object target, string eventName) =>
		UnsupportedAdapterAction(target, "routed events");

	public virtual ActionResult RaiseExpressionRoutedEvent(object target, object? expressionPayload, int? timeoutMs) =>
		UnsupportedAdapterAction(target, "routed events");

	public virtual ActionResult RunKnownOperation(object target, string? operation)
	{
		switch (operation?.Trim())
		{
			case "BringIntoView":
				if (TryInvokeNoArg(target, "BringIntoView"))
					return ActionResult.Ok();
				break;
			case "Select":
				if (TrySetBooleanProperty(target, [KnownProperties.IsSelected], true) || TryInvokeNoArg(target, "Select"))
					return ActionResult.Ok();
				break;
			case "Expand":
				if (TrySetBooleanProperty(target, [KnownProperties.IsExpanded], true) || TryInvokeNoArg(target, "Expand"))
					return ActionResult.Ok();
				break;
			case "Collapse":
				if (TrySetBooleanProperty(target, [KnownProperties.IsExpanded], false) || TryInvokeNoArg(target, "Collapse"))
					return ActionResult.Ok();
				break;
			case "Check":
				if (TrySetBooleanProperty(target, [KnownProperties.IsChecked, KnownProperties.Checked], true) || TryInvokeNoArg(target, "Check"))
					return ActionResult.Ok();
				break;
			case "Uncheck":
				if (TrySetBooleanProperty(target, [KnownProperties.IsChecked, KnownProperties.Checked], false) || TryInvokeNoArg(target, "Uncheck"))
					return ActionResult.Ok();
				break;
		}

		return ActionResult.Unsupported($"Known operation '{operation}' is not supported for target type '{target.GetType().FullName}'.");
	}

	protected static ActionResult UnsupportedAdapterAction(object target, string actionName) =>
		TargetActionCommand.UnsupportedAdapterAction(target, actionName);

	protected static bool TrySetClrProperty(object target, string propertyName, object? value, out ActionResult result)
	{
		var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
		if (property is null)
		{
			result = default;
			return false;
		}

		if (!property.CanWrite || property.GetIndexParameters().Length != 0)
		{
			result = ActionResult.Unsupported($"Property '{propertyName}' is read-only.");
			return true;
		}

		property.SetValue(target, TargetValueConverter.ConvertValue(value, property.PropertyType), null);
		result = ActionResult.Ok();
		return true;
	}

	protected static bool TrySetBooleanProperty(object target, IReadOnlyList<string> propertyNames, bool value)
	{
		foreach (var propertyName in propertyNames)
		{
			var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
			if (property is null || !property.CanWrite || property.GetIndexParameters().Length != 0)
				continue;

			var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
			if (propertyType != typeof(bool))
				continue;

			try
			{
				property.SetValue(target, value, null);
				return true;
			}
			catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidOperationException)
			{
			}
		}

		return false;
	}

	protected static bool TryInvokeNoArg(object target, string methodName)
	{
		var method = target.GetType()
			.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
			.FirstOrDefault(method => string.Equals(method.Name, methodName, StringComparison.Ordinal) && method.GetParameters().Length == 0);
		if (method is null)
			return false;

		try
		{
			method.Invoke(target, null);
			return true;
		}
		catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidOperationException)
		{
			return false;
		}
	}
}
