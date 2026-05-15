namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DeepFlowTest.Assert;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using LinqExpression = System.Linq.Expressions.Expression;

public class Element
{
	private readonly AppDriver? driver;
	private VisualTreeNodeDto node;

	internal Element(
		AppDriver driver,
		VisualTreeNodeDto node,
		ElementSelector? selector = null,
		VisualTreeSnapshot? snapshot = null,
		ElementRepairInfo? repairInfo = null,
		IReadOnlyList<ElementPathSegmentResponse>? diagnosticPath = null,
		bool register = true)
	{
		this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
		this.node = node ?? throw new ArgumentNullException(nameof(node));
		Selector = selector;
		Snapshot = snapshot;
		RepairInfo = repairInfo;
		DiagnosticPath = diagnosticPath ?? [];
		if (register)
			driver.RegisterElement(this);
	}

	internal Element(VisualTreeNodeDto node, VisualTreeSnapshot snapshot)
	{
		this.node = node ?? throw new ArgumentNullException(nameof(node));
		Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
		DiagnosticPath = [];
	}

	protected Element(Element source)
	{
		_ = source ?? throw new ArgumentNullException(nameof(source));
		driver = source.driver;
		node = source.node;
		Selector = source.Selector;
		Snapshot = source.Snapshot;
		RepairInfo = source.RepairInfo;
		DiagnosticPath = source.DiagnosticPath;
		driver?.RegisterElement(this);
	}

	public string TargetId => node.TargetId;

	public string TypeName => node.TypeName;

	public string? FrameworkTypeName => node.FrameworkTypeName;

	public IReadOnlyDictionary<string, object?> Properties => node.Properties;

	public ElementSelector? Selector { get; }

	internal ElementRepairInfo? RepairInfo { get; }

	internal VisualTreeNodeDto SnapshotNode => node;

	internal VisualTreeSnapshot? CurrentSnapshot => Snapshot;

	internal IReadOnlyList<ElementPathSegmentResponse> DiagnosticPath { get; }

	protected VisualTreeSnapshot? Snapshot { get; private set; }

	private AppDriver Driver =>
		driver ?? throw new InvalidOperationException("This element is only available while evaluating a target-side expression and cannot perform driver actions.");

	public Element? Parent
	{
		get
		{
			if (Snapshot is null || node.ParentId is null)
				return null;

			var parent = Snapshot.Nodes.SingleOrDefault(candidate => candidate.TargetId == node.ParentId);
			if (parent is null)
				return null;

			return driver is null
				? new Element(parent, Snapshot)
				: new Element(driver, parent, snapshot: Snapshot);
		}
	}

	public IReadOnlyList<Element> Children
	{
		get
		{
			var snapshot = Snapshot ?? Driver.GetVisualTree();
			Snapshot = snapshot;
			var byId = snapshot.Nodes.ToDictionary(static candidate => candidate.TargetId, StringComparer.Ordinal);
			if (byId.TryGetValue(node.TargetId, out var refreshedNode))
				node = refreshedNode;

			return node.ChildIds
				.Where(byId.ContainsKey)
				.Select(childId => driver is null
					? new Element(byId[childId], snapshot)
					: new Element(driver, byId[childId], snapshot: snapshot))
				.ToArray();
		}
	}

	public IReadOnlyList<Element> Child => Children;

	public IReadOnlyList<Element> Descendants => Children.SelectMany(static child => new[] { child }.Concat(child.Descendants)).ToArray();

	public Element this[int childIndex] => Children[childIndex];

	public Primitive this[string propertyName]
	{
		get => Primitive.FromProperty(this, propertyName);
		set => SetProperty(propertyName, value?.Value);
	}

	public bool HasProperty(string propertyName) => Properties.ContainsKey(propertyName);

	public T? GetProperty<T>(string propertyName)
	{
		if (!Properties.TryGetValue(propertyName, out var value) || value is null)
			return default;

		if (value is T typed)
			return typed;

		return (T?)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
	}

	public virtual Element Click() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId });

	public virtual Element RightClick() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId, MouseButton = "right" });

	public virtual Element DoubleClick() =>
		UsesNativeClickPayload()
			? SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId, ClickCount = 2 })
			: RaiseEvent("MouseDoubleClick");

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
		SendWithRepair<ScreenshotCommandResponse>(() => new ScreenshotCommandRequest { TargetId = TargetId, Format = format });

	public byte[] Screenshot(ImageFormat format = ImageFormat.Jpeg) =>
		Convert.FromBase64String(AppDriver.WaitForStableScreenshot(() => CaptureScreenshot(format.ToProtocolString()), nameof(Screenshot)).BytesBase64 ?? string.Empty);

	public virtual Element Screenshot(string fileOutputPath)
	{
		_ = fileOutputPath ?? throw new ArgumentNullException(nameof(fileOutputPath));
		var bytes = Screenshot(GetImageFormatFromPath(fileOutputPath));
		var directory = Path.GetDirectoryName(Path.GetFullPath(fileOutputPath));
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);
		File.WriteAllBytes(fileOutputPath, bytes);
		return this;
	}

	public virtual Element Screenshot(out byte[] screenshotBytes, ImageFormat format = ImageFormat.Jpeg)
	{
		screenshotBytes = Screenshot(format);
		return this;
	}

	public virtual Element SelectText(string text)
	{
		var currentText = GetProperty<string>("Text") ?? string.Empty;
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

	public virtual Element Invoke<TInput>(Expression<Action<TInput>> code, int timeoutMs = 10_000) =>
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = timeoutMs });

	public virtual TOutput? Invoke<TInput, TOutput>(Expression<Func<TInput, TOutput>> code, int timeoutMs = 10_000)
	{
		var response = SendTargetedWithRepairResponse(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = timeoutMs });
		ThrowIfUnserializableResult(response, nameof(Invoke));
		return ConvertResponseValue<TOutput>(response.Value);
	}

	public virtual Element Invoke<TInput, TOutput>(Expression<Func<TInput, TOutput>> code, out TOutput? result, int timeoutMs = 10_000)
	{
		result = Invoke(code, timeoutMs);
		return this;
	}

	public virtual Element InvokeAsync<TInput>(Expression<Func<TInput, Task>> code, int timeoutMs = 10_000)
	{
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = timeoutMs });
		return this;
	}

	public virtual TOutput? InvokeAsync<TInput, TOutput>(Expression<Func<TInput, Task<TOutput>>> code, int timeoutMs = 10_000)
	{
		var response = SendTargetedWithRepairResponse(() => new InvokeCommandRequest { TargetId = TargetId, Code = Eval.SerializeCode(code), AllowUnsafeCode = true, TimeoutMs = timeoutMs });
		ThrowIfUnserializableResult(response, nameof(InvokeAsync));
		return ConvertResponseValue<TOutput>(response.Value);
	}

	public virtual Element InvokeAsync<TInput, TOutput>(Expression<Func<TInput, Task<TOutput>>> code, out TOutput? result, int timeoutMs = 10_000)
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

	public virtual Element Assert(Expression<Func<Element, bool?>> predicateExpression, int timeoutMs = 10_000)
	{
		_ = predicateExpression ?? throw new ArgumentNullException(nameof(predicateExpression));
		var parameter = DebugValueExpressionVisitor.GetDebugExpression(TypeName, this);
		var assertable = Assertable.FromValueExpression(this, parameter, RefreshFromCurrentSnapshot);
		assertable.IsTrue(predicateExpression, timeoutMs);
		return this;
	}

	internal static Element FromMatch(
		AppDriver driver,
		FindElementMatchResponse match,
		ElementSelector? selector,
		ElementRepairInfo? repairInfo = null)
	{
		return new Element(
			driver,
			new VisualTreeNodeDto
			{
				TargetId = match.TargetId,
				TypeName = match.TypeName,
				FrameworkTypeName = match.FrameworkTypeName,
				Properties = match.Properties,
			},
			selector,
			repairInfo: repairInfo,
			diagnosticPath: match.Path);
	}

	internal static Element FromNode(
		AppDriver driver,
		VisualTreeNodeDto node,
		VisualTreeSnapshot snapshot,
		ElementRepairInfo? repairInfo = null,
		bool register = true) =>
		new(driver, node, snapshot: snapshot, repairInfo: repairInfo, register: register);

	internal static Element FromSnapshot(VisualTreeNodeDto node, VisualTreeSnapshot snapshot) =>
		new(node, snapshot);

	protected void ReplaceNode(VisualTreeNodeDto replacement, VisualTreeSnapshot? snapshot = null)
	{
		var previousTargetId = node.TargetId;
		node = replacement;
		Snapshot = snapshot;
		Driver.MoveElementRegistration(this, previousTargetId, replacement.TargetId);
	}

	internal void RefreshFromCache(VisualTreeNodeDto replacement, VisualTreeSnapshot snapshot)
	{
		node = replacement;
		Snapshot = snapshot;
	}

	private Element KnownOperation(string operation) =>
		SendTargetedWithRepair(() => new KnownOperationCommandRequest { TargetId = TargetId, Operation = operation });

	private bool UsesNativeClickPayload() =>
		string.Equals(TypeName, "HWND", StringComparison.Ordinal)
		|| FrameworkTypeName?.StartsWith("System.Windows.Forms.", StringComparison.Ordinal) == true;

	private Element SendTargetedWithRepair(Func<IpcCommand> commandFactory)
	{
		SendTargetedWithRepairResponse(commandFactory);
		return this;
	}

	private StandardIpcResponse SendTargetedWithRepairResponse(Func<IpcCommand> commandFactory)
	{
		var response = SendWithRepair<StandardIpcResponse>(commandFactory);
		if (response.Success != true)
			throw new AppDriverException(response.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, response.Error ?? "Command failed.");
		return response;
	}

	private TResponse SendWithRepair<TResponse>(Func<IpcCommand> commandFactory)
	{
		var response = Driver.Send<TResponse>(commandFactory());
		if (IsFailure(response, ProtocolConstants.ErrorCodes.StaleTarget))
		{
			var repaired = Driver.Repair(this);
			ReplaceNode(repaired.node);
			response = Driver.Send<TResponse>(commandFactory());
		}

		if (response is StandardIpcResponse { Success: false } error)
			throw new AppDriverException(error.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, error.Error ?? "Command failed.");
		if (IsFailure(response, out var errorCode, out var errorMessage))
			throw new AppDriverException(errorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, errorMessage ?? "Command failed.");

		return response;
	}

	private static bool IsFailure<TResponse>(TResponse response, string errorCode)
	{
		return IsFailure(response, out var actualErrorCode, out _) &&
			string.Equals(actualErrorCode, errorCode, StringComparison.Ordinal);
	}

	private static bool IsFailure<TResponse>(TResponse response, out string? errorCode, out string? errorMessage)
	{
		errorCode = null;
		errorMessage = null;
		if (response is null)
			return false;

		var responseType = response.GetType();
		var success = responseType.GetProperty(nameof(StandardIpcResponse.Success))?.GetValue(response);
		if (success is not bool successValue || successValue)
			return false;

		errorCode = responseType.GetProperty(nameof(StandardIpcResponse.ErrorCode))?.GetValue(response)?.ToString();
		errorMessage = responseType.GetProperty(nameof(StandardIpcResponse.Error))?.GetValue(response)?.ToString();
		return true;
	}

	private static ImageFormat GetImageFormatFromPath(string path)
	{
		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			".bmp" => ImageFormat.Bmp,
			".gif" => ImageFormat.Gif,
			".jpg" or ".jpeg" => ImageFormat.Jpeg,
			_ => ImageFormat.Png,
		};
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

	private static T? ConvertResponseValue<T>(object? value)
	{
		if (value is null)
			return default;
		if (value is T typed)
			return typed;
		if (value is Newtonsoft.Json.Linq.JToken token)
			return token.ToObject<T>();
		return (T?)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
	}

	private static void ThrowIfUnserializableResult(StandardIpcResponse response, string caller)
	{
		if (string.Equals(response.Status, ProtocolConstants.Statuses.UnserializableResult, StringComparison.Ordinal))
			throw new SerializationException($"Unserializable {caller} result received.");
	}

	private sealed class ElementPropertyAccessCollector : ExpressionVisitor
	{
		private readonly HashSet<string> propertyNames = new(StringComparer.Ordinal);

		public static IReadOnlyCollection<string> Collect(LinqExpression expression)
		{
			var collector = new ElementPropertyAccessCollector();
			collector.Visit(expression);
			return collector.propertyNames;
		}

		protected override LinqExpression VisitIndex(IndexExpression node)
		{
			if (IsElementExpression(node.Object) && node.Arguments.Count == 1 && TryGetString(node.Arguments[0], out var propertyName))
				propertyNames.Add(propertyName);

			return base.VisitIndex(node);
		}

		protected override LinqExpression VisitMethodCall(MethodCallExpression node)
		{
			if (IsElementExpression(node.Object)
				&& node.Arguments.Count == 1
				&& (node.Method.Name == "get_Item" || node.Method.Name == nameof(HasProperty))
				&& TryGetString(node.Arguments[0], out var propertyName))
			{
				propertyNames.Add(propertyName);
			}

			return base.VisitMethodCall(node);
		}

		private static bool IsElementExpression(LinqExpression? expression) =>
			expression is not null && typeof(Element).IsAssignableFrom(expression.Type);

		private static bool TryGetString(LinqExpression expression, out string value)
		{
			while (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert)
				expression = convert.Operand;

			if (expression is ConstantExpression { Value: string constant })
			{
				value = constant;
				return true;
			}

			value = string.Empty;
			return false;
		}
	}
}

#if NET5_0_OR_GREATER
public class Element<T> : Element
	where T : Element<T>
{
	public Element(Element source)
		: base(source)
	{
	}

	internal Element(AppDriver driver, VisualTreeNodeDto node, ElementSelector? selector = null, VisualTreeSnapshot? snapshot = null)
		: base(driver, node, selector, snapshot)
	{
	}

	public override T Click() => Return(base.Click());
	public override T RightClick() => Return(base.RightClick());
	public override T DoubleClick() => Return(base.DoubleClick());
	public override T Focus() => Return(base.Focus());
	public override T Select() => Return(base.Select());
	public override T Expand() => Return(base.Expand());
	public override T Collapse() => Return(base.Collapse());
	public override T Check() => Return(base.Check());
	public override T Uncheck() => Return(base.Uncheck());
	public override T ScrollIntoView() => Return(base.ScrollIntoView());
	public override T AcceptDialog() => Return(base.AcceptDialog());
	public override T CancelDialog() => Return(base.CancelDialog());
	public override T Type(string text, bool clearFirst = false) => Return(base.Type(text, clearFirst));
	public override T SelectText(string text) => Return(base.SelectText(text));
	public override T Screenshot(string fileOutputPath) => Return(base.Screenshot(fileOutputPath));
	public override T Screenshot(out byte[] screenshotBytes, ImageFormat format = ImageFormat.Jpeg) => Return(base.Screenshot(out screenshotBytes, format));
	public override T RaiseEvent(string eventName) => Return(base.RaiseEvent(eventName));
	public override T RaiseEvent<TInput>(Expression<Func<TInput, RoutedEventArgs>> code) => Return(base.RaiseEvent(code));
	public override T Invoke(string methodName, bool allowUnsafeCode = false) => Return(base.Invoke(methodName, allowUnsafeCode));
	public override T Invoke<TInput>(Expression<Action<TInput>> code, int timeoutMs = 10_000) => Return(base.Invoke(code, timeoutMs));
	public override T Invoke<TInput, TOutput>(Expression<Func<TInput, TOutput>> code, out TOutput result, int timeoutMs = 10_000)
	{
		var returned = base.Invoke(code, out TOutput? value, timeoutMs);
		result = value!;
		return Return(returned);
	}
	public override T InvokeAsync<TInput>(Expression<Func<TInput, Task>> code, int timeoutMs = 10_000) => Return(base.InvokeAsync(code, timeoutMs));
	public override T InvokeAsync<TInput, TOutput>(Expression<Func<TInput, Task<TOutput>>> code, out TOutput result, int timeoutMs = 10_000)
	{
		var returned = base.InvokeAsync(code, out TOutput? value, timeoutMs);
		result = value!;
		return Return(returned);
	}
	public override T SetProperty(string propertyName, object? value) => Return(base.SetProperty(propertyName, value));
	public override T SetProperty<TInput, TOutput>(string propertyName, Expression<Func<TInput, TOutput>> getValue) => Return(base.SetProperty(propertyName, getValue));
	public override T Assert(Expression<Func<Element, bool?>> predicateExpression, int timeoutMs = 10_000) => Return(base.Assert(predicateExpression, timeoutMs));

	private T Return(Element _)
	{
		if (this is T typed)
			return typed;

		throw new InvalidCastException($"Element wrapper '{GetType().FullName}' cannot be returned as '{typeof(T).FullName}'.");
	}
}
#else
public class Element<T> : Element
	where T : Element
{
	public Element(Element source)
		: base(source)
	{
	}

	internal Element(AppDriver driver, VisualTreeNodeDto node, ElementSelector? selector = null, VisualTreeSnapshot? snapshot = null)
		: base(driver, node, selector, snapshot)
	{
	}

	public new T Click() => Return(base.Click());
	public new T RightClick() => Return(base.RightClick());
	public new T DoubleClick() => Return(base.DoubleClick());
	public new T Focus() => Return(base.Focus());
	public new T Select() => Return(base.Select());
	public new T Expand() => Return(base.Expand());
	public new T Collapse() => Return(base.Collapse());
	public new T Check() => Return(base.Check());
	public new T Uncheck() => Return(base.Uncheck());
	public new T ScrollIntoView() => Return(base.ScrollIntoView());
	public new T AcceptDialog() => Return(base.AcceptDialog());
	public new T CancelDialog() => Return(base.CancelDialog());
	public new T Type(string text, bool clearFirst = false) => Return(base.Type(text, clearFirst));
	public new T SelectText(string text) => Return(base.SelectText(text));
	public new T Screenshot(string fileOutputPath) => Return(base.Screenshot(fileOutputPath));
	public new T Screenshot(out byte[] screenshotBytes, ImageFormat format = ImageFormat.Jpeg) => Return(base.Screenshot(out screenshotBytes, format));
	public new T RaiseEvent(string eventName) => Return(base.RaiseEvent(eventName));
	public new T RaiseEvent<TInput>(Expression<Func<TInput, RoutedEventArgs>> code) => Return(base.RaiseEvent(code));
	public new T Invoke(string methodName, bool allowUnsafeCode = false) => Return(base.Invoke(methodName, allowUnsafeCode));
	public new T Invoke<TInput>(Expression<Action<TInput>> code, int timeoutMs = 10_000) => Return(base.Invoke(code, timeoutMs));
	public new T Invoke<TInput, TOutput>(Expression<Func<TInput, TOutput>> code, out TOutput? result, int timeoutMs = 10_000) => Return(base.Invoke(code, out result, timeoutMs));
	public new T InvokeAsync<TInput>(Expression<Func<TInput, Task>> code, int timeoutMs = 10_000) => Return(base.InvokeAsync(code, timeoutMs));
	public new T InvokeAsync<TInput, TOutput>(Expression<Func<TInput, Task<TOutput>>> code, out TOutput? result, int timeoutMs = 10_000) => Return(base.InvokeAsync(code, out result, timeoutMs));
	public new T SetProperty(string propertyName, object? value) => Return(base.SetProperty(propertyName, value));
	public new T SetProperty<TInput, TOutput>(string propertyName, Expression<Func<TInput, TOutput>> getValue) => Return(base.SetProperty(propertyName, getValue));
	public new T Assert(Expression<Func<Element, bool?>> predicateExpression, int timeoutMs = 10_000) => Return(base.Assert(predicateExpression, timeoutMs));

	private T Return(Element _)
	{
		if (this is T typed)
			return typed;

		throw new InvalidCastException($"Element wrapper '{GetType().FullName}' cannot be returned as '{typeof(T).FullName}'.");
	}
}
#endif
