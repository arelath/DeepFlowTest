namespace DeepFlowTest;

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows;
using DeepFlowTest.Assert;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public partial class Element
{
	public virtual Element Click() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId });

	public virtual Element RightClick() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId, MouseButton = MouseButtonKind.Right });

	public virtual Element MiddleClick() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId, MouseButton = MouseButtonKind.Middle });

	public virtual Element DoubleClick() =>
		UsesNativeClickPayload()
			? SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId, ClickCount = 2 })
			: RaiseEvent("MouseDoubleClick");

	public virtual Element DragAndDropTo(Element destination, DragAndDropOptions? options = null)
	{
		_ = destination ?? throw new ArgumentNullException(nameof(destination));
		return SendTargetedWithRepair(() => CreateDragAndDropCommand(destination.TargetId, options));
	}

	public virtual Element DragAndDropTo(ElementSelector destinationSelector, DragAndDropOptions? options = null)
	{
		_ = destinationSelector ?? throw new ArgumentNullException(nameof(destinationSelector));
		var destination = Driver.GetElement(destinationSelector);
		return DragAndDropTo(destination, options);
	}

	public virtual Element Focus() => SendTargetedWithRepair(() => new FocusCommandRequest { TargetId = TargetId });

	public virtual Element Select() => KnownOperation("Select");

	public virtual Element Expand() => KnownOperation("Expand");

	public virtual Element Collapse() => KnownOperation("Collapse");

	public virtual Element Check() => KnownOperation("Check");

	public virtual Element Uncheck() => KnownOperation("Uncheck");

	public virtual Element ScrollIntoView() => KnownOperation("BringIntoView");

	public virtual Element AcceptDialog() => KnownOperation("AcceptDialog");

	public virtual Element CancelDialog() => KnownOperation("CancelDialog");

	public virtual Element Type(string text, bool clearFirst = false)
	{
		SendTargetedWithRepair(() => new TypeTextCommandRequest { TargetId = TargetId, Text = text, ClearFirst = clearFirst });
		return this;
	}

	public ScreenshotCommandResponse CaptureScreenshot(string format = "png") =>
		Commands.CaptureScreenshot(this, format);

	public byte[] Screenshot(ImageFormat format = ImageFormat.Jpeg) =>
		Commands.Screenshot(this, format);

	public virtual Element Screenshot(string fileOutputPath)
	{
		_ = fileOutputPath ?? throw new ArgumentNullException(nameof(fileOutputPath));
		Commands.SaveScreenshot(this, fileOutputPath);
		return this;
	}

	public virtual Element Screenshot(out byte[] screenshotBytes, ImageFormat format = ImageFormat.Jpeg)
	{
		screenshotBytes = Screenshot(format);
		return this;
	}

	public virtual Element SelectText(string text)
	{
		var currentText = GetProperty<string>(KnownProperties.Text) ?? string.Empty;
		var startIndex = currentText.IndexOf(text ?? string.Empty, StringComparison.Ordinal);
		if (startIndex < 0)
			return SetProperty("SelectedText", text);

		SetProperty("SelectionStart", startIndex);
		SetProperty("SelectionLength", text?.Length ?? 0);
		return this;
	}

	public virtual Element RaiseEvent(string eventName) =>
		SendTargetedWithRepair(() => new RaiseEventCommandRequest { TargetId = TargetId, EventName = eventName });

	public virtual Element RaiseEvent<TInput>(Expression<Func<TInput, RoutedEventArgs>> code) =>
		SendTargetedWithRepair(() => new RaiseEventCommandRequest { TargetId = TargetId, GetRoutedEventArgs = Eval.SerializeCode(code) });

	public virtual Element Invoke(string methodName, bool allowUnsafeCode = false) =>
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = methodName, AllowUnsafeCode = allowUnsafeCode });

	public virtual Element Invoke<TInput>(Expression<Action<TInput>> code, TimeSpan? timeout = null) =>
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = EffectiveCommandTimeout(timeout) });

	public virtual TOutput? Invoke<TInput, TOutput>(Expression<Func<TInput, TOutput>> code, TimeSpan? timeout = null)
	{
		var response = SendTargetedWithRepairResponse(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = EffectiveCommandTimeout(timeout) });
		ElementCommandExecutor.ThrowIfUnserializableResult(response, nameof(Invoke));
		return ElementCommandExecutor.ConvertResponseValue<TOutput>(response.Value);
	}

	public virtual Element Invoke<TInput, TOutput>(Expression<Func<TInput, TOutput>> code, out TOutput? result, TimeSpan? timeout = null)
	{
		result = Invoke(code, timeout);
		return this;
	}

	/// <summary>
	/// Queues <paramref name="code"/> on the target's dispatcher and returns as soon as it is queued, without
	/// waiting for it or reporting its outcome. Use this when the target is expected to handle the outcome
	/// itself - an intentional crash, or a shutdown that tears down the pipe - because a normal
	/// <see cref="Invoke{TInput}"/> would capture the exception as a command failure instead of letting it reach
	/// the target's unhandled-exception handling.
	/// </summary>
	public virtual Element InvokeDetached<TInput>(Expression<Action<TInput>> code) =>
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, Detached = true });

	public virtual Element InvokeAsync<TInput>(Expression<Func<TInput, Task>> code, TimeSpan? timeout = null)
	{
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = EffectiveCommandTimeout(timeout) });
		return this;
	}

	public virtual TOutput? InvokeAsync<TInput, TOutput>(Expression<Func<TInput, Task<TOutput>>> code, TimeSpan? timeout = null)
	{
		var response = SendTargetedWithRepairResponse(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = EffectiveCommandTimeout(timeout) });
		ElementCommandExecutor.ThrowIfUnserializableResult(response, nameof(InvokeAsync));
		return ElementCommandExecutor.ConvertResponseValue<TOutput>(response.Value);
	}

	public virtual Element InvokeAsync<TInput, TOutput>(Expression<Func<TInput, Task<TOutput>>> code, out TOutput? result, TimeSpan? timeout = null)
	{
		result = InvokeAsync(code, timeout);
		return this;
	}

	public virtual Element SetProperty(string propertyName, object? value) =>
		SendTargetedWithRepair(() => new SetPropertyCommandRequest { TargetId = TargetId, PropertyName = propertyName, PropertyValue = value });

	public virtual Element SetProperty<TInput, TOutput>(string propertyName, Expression<Func<TInput, TOutput>> getValue) =>
		SendTargetedWithRepair(() => new SetPropertyCommandRequest
		{
			TargetId = TargetId,
			PropertyName = propertyName,
			PropertyValue = Eval.SerializeCode(getValue),
		});

	public virtual Element Assert(Expression<Func<Element, bool?>> predicateExpression, TimeSpan? timeout = null)
	{
		_ = predicateExpression ?? throw new ArgumentNullException(nameof(predicateExpression));
		var parameter = DebugValueExpressionVisitor.GetDebugExpression(TypeName, this);
		var assertable = Assertable.FromValueExpression(this, parameter, RefreshFromCurrentSnapshot);
		assertable.IsTrue(predicateExpression, timeout);
		return this;
	}

	private Element KnownOperation(string operation) =>
		SendTargetedWithRepair(() => new KnownOperationCommandRequest { TargetId = TargetId, Operation = operation });

	private bool UsesNativeClickPayload() =>
		string.Equals(TypeName, "HWND", StringComparison.Ordinal)
		|| FrameworkTypeName?.StartsWith("System.Windows.Forms.", StringComparison.Ordinal) == true;

	private Element SendTargetedWithRepair(Func<IpcCommand> commandFactory)
	{
		return Commands.SendTargetedWithRepair(this, commandFactory);
	}

	private DragAndDropCommandRequest CreateDragAndDropCommand(string destinationTargetId, DragAndDropOptions? options)
	{
		var request = new DragAndDropCommandRequest
		{
			TargetId = TargetId,
			DestinationTargetId = destinationTargetId,
		};
		if (options is null)
			return request;

		options.Validate();
		request.DurationMs = DurationUtility.ToMilliseconds(options.Duration, nameof(options.Duration), allowZero: true);
		request.HoldMs = DurationUtility.ToMilliseconds(options.HoldDuration, nameof(options.HoldDuration), allowZero: true);
		request.StepIntervalMs = DurationUtility.ToMilliseconds(options.StepInterval, nameof(options.StepInterval));
		request.PostDropWaitMs = DurationUtility.ToMilliseconds(options.PostDropDelay, nameof(options.PostDropDelay), allowZero: true);
		request.SourceAnchorX = options.SourceAnchorX;
		request.SourceAnchorY = options.SourceAnchorY;
		request.DestinationAnchorX = options.DestinationAnchorX;
		request.DestinationAnchorY = options.DestinationAnchorY;
		request.UseInjectedEvents = options.UseInjectedEvents;
		request.EnsureForeground = options.EnsureForeground;
		request.ValidateSameProcess = options.ValidateSameProcess;
		request.TimeoutMs = options.Timeout is TimeSpan timeout ? DurationUtility.ToMilliseconds(timeout, nameof(options.Timeout)) : null;
		return request;
	}

	private StandardIpcResponse SendTargetedWithRepairResponse(Func<IpcCommand> commandFactory)
	{
		return Commands.SendTargetedWithRepairResponse(this, commandFactory);
	}

	private static int EffectiveCommandTimeout(TimeSpan? timeout) =>
		DurationUtility.ToMilliseconds(
			timeout ?? TimeSpan.FromMilliseconds(TimeoutDefaults.CommandTimeoutMs),
			nameof(timeout));

	private void RefreshFromCurrentSnapshot()
	{
		var snapshot = Driver.GetVisualTree(TargetId);
		var refreshed = snapshot.Nodes.SingleOrDefault(candidate => string.Equals(candidate.TargetId, TargetId, StringComparison.Ordinal));
		if (refreshed is not null)
		{
			ReplaceNode(refreshed, snapshot);
			return;
		}

		if (Selector is null)
			return;

		var repaired = Driver.Repair(this);
		ReplaceNode(repaired.node, repaired.Snapshot);
	}
}
