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

	public virtual Element Invoke<TInput>(Expression<Action<TInput>> code, int timeoutMs = TimeoutDefaults.CommandTimeoutMs) =>
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = timeoutMs });

	public virtual TOutput? Invoke<TInput, TOutput>(Expression<Func<TInput, TOutput>> code, int timeoutMs = TimeoutDefaults.CommandTimeoutMs)
	{
		var response = SendTargetedWithRepairResponse(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = timeoutMs });
		ElementCommandExecutor.ThrowIfUnserializableResult(response, nameof(Invoke));
		return ElementCommandExecutor.ConvertResponseValue<TOutput>(response.Value);
	}

	public virtual Element Invoke<TInput, TOutput>(Expression<Func<TInput, TOutput>> code, out TOutput? result, int timeoutMs = TimeoutDefaults.CommandTimeoutMs)
	{
		result = Invoke(code, timeoutMs);
		return this;
	}

	public virtual Element InvokeAsync<TInput>(Expression<Func<TInput, Task>> code, int timeoutMs = TimeoutDefaults.CommandTimeoutMs)
	{
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = timeoutMs });
		return this;
	}

	public virtual TOutput? InvokeAsync<TInput, TOutput>(Expression<Func<TInput, Task<TOutput>>> code, int timeoutMs = TimeoutDefaults.CommandTimeoutMs)
	{
		var response = SendTargetedWithRepairResponse(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = timeoutMs });
		ElementCommandExecutor.ThrowIfUnserializableResult(response, nameof(InvokeAsync));
		return ElementCommandExecutor.ConvertResponseValue<TOutput>(response.Value);
	}

	public virtual Element InvokeAsync<TInput, TOutput>(Expression<Func<TInput, Task<TOutput>>> code, out TOutput? result, int timeoutMs = TimeoutDefaults.CommandTimeoutMs)
	{
		result = InvokeAsync(code, timeoutMs);
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

	public virtual Element Assert(Expression<Func<Element, bool?>> predicateExpression, int timeoutMs = TimeoutDefaults.CommandTimeoutMs)
	{
		_ = predicateExpression ?? throw new ArgumentNullException(nameof(predicateExpression));
		var parameter = DebugValueExpressionVisitor.GetDebugExpression(TypeName, this);
		var assertable = Assertable.FromValueExpression(this, parameter, RefreshFromCurrentSnapshot);
		assertable.IsTrue(predicateExpression, timeoutMs);
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

		request.DurationMs = options.DurationMs;
		request.HoldMs = options.HoldMs;
		request.StepIntervalMs = options.StepIntervalMs;
		request.PostDropWaitMs = options.PostDropWaitMs;
		request.SourceAnchorX = options.SourceAnchorX;
		request.SourceAnchorY = options.SourceAnchorY;
		request.DestinationAnchorX = options.DestinationAnchorX;
		request.DestinationAnchorY = options.DestinationAnchorY;
		request.UseInjectedEvents = options.UseInjectedEvents;
		request.EnsureForeground = options.EnsureForeground;
		request.ValidateSameProcess = options.ValidateSameProcess;
		request.TimeoutMs = options.TimeoutMs;
		return request;
	}

	private StandardIpcResponse SendTargetedWithRepairResponse(Func<IpcCommand> commandFactory)
	{
		return Commands.SendTargetedWithRepairResponse(this, commandFactory);
	}

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
