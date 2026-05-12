namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Windows;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public class Element
{
	private readonly AppDriver driver;
	private VisualTreeNodeDto node;

	internal Element(AppDriver driver, VisualTreeNodeDto node, ElementSelector? selector = null, VisualTreeSnapshot? snapshot = null)
	{
		this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
		this.node = node ?? throw new ArgumentNullException(nameof(node));
		Selector = selector;
		Snapshot = snapshot;
	}

	protected Element(Element source)
	{
		_ = source ?? throw new ArgumentNullException(nameof(source));
		driver = source.driver;
		node = source.node;
		Selector = source.Selector;
		Snapshot = source.Snapshot;
	}

	public string TargetId => node.TargetId;

	public string TypeName => node.TypeName;

	public string? FrameworkTypeName => node.FrameworkTypeName;

	public IReadOnlyDictionary<string, object?> Properties => node.Properties;

	public ElementSelector? Selector { get; }

	protected VisualTreeSnapshot? Snapshot { get; private set; }

	public Element? Parent
	{
		get
		{
			if (Snapshot is null || node.ParentId is null)
				return null;

			var parent = Snapshot.Nodes.SingleOrDefault(candidate => candidate.TargetId == node.ParentId);
			return parent is null ? null : new Element(driver, parent, snapshot: Snapshot);
		}
	}

	public IReadOnlyList<Element> Children
	{
		get
		{
			var snapshot = Snapshot ?? driver.GetVisualTree();
			Snapshot = snapshot;
			var byId = snapshot.Nodes.ToDictionary(static candidate => candidate.TargetId, StringComparer.Ordinal);
			if (byId.TryGetValue(node.TargetId, out var refreshedNode))
				node = refreshedNode;

			return node.ChildIds
				.Where(byId.ContainsKey)
				.Select(childId => new Element(driver, byId[childId], snapshot: snapshot))
				.ToArray();
		}
	}

	public IReadOnlyList<Element> Descendants => Children.SelectMany(static child => new[] { child }.Concat(child.Descendants)).ToArray();

	public Element this[int childIndex] => Children[childIndex];

	public Primitive this[string propertyName] => Primitive.FromProperty(this, propertyName);

	public bool HasProperty(string propertyName) => Properties.ContainsKey(propertyName);

	public T? GetProperty<T>(string propertyName)
	{
		if (!Properties.TryGetValue(propertyName, out var value) || value is null)
			return default;

		if (value is T typed)
			return typed;

		return (T?)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
	}

	public Element Click() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId });

	public Element RightClick() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId, MouseButton = "right" });

	public Element DoubleClick() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId, ClickCount = 2 });

	public Element Focus() => SendTargetedWithRepair(() => new FocusCommandRequest { TargetId = TargetId });

	public Element Select() => KnownOperation("Select");

	public Element Expand() => KnownOperation("Expand");

	public Element Collapse() => KnownOperation("Collapse");

	public Element Check() => KnownOperation("Check");

	public Element Uncheck() => KnownOperation("Uncheck");

	public Element ScrollIntoView() => KnownOperation("BringIntoView");

	public Element AcceptDialog() => KnownOperation("AcceptDialog");

	public Element CancelDialog() => KnownOperation("CancelDialog");

	public Element Type(string text, bool clearFirst = false)
	{
		SendTargetedWithRepair(() => new TypeTextCommandRequest { TargetId = TargetId, Text = text, ClearFirst = clearFirst });
		return this;
	}

	public ScreenshotCommandResponse CaptureScreenshot(string format = "png") =>
		SendWithRepair<ScreenshotCommandResponse>(() => new ScreenshotCommandRequest { TargetId = TargetId, Format = format });

	public byte[] Screenshot(ImageFormat format = ImageFormat.Jpeg) =>
		Convert.FromBase64String(CaptureScreenshot(format.ToProtocolString()).BytesBase64 ?? string.Empty);

	public Element Screenshot(string fileOutputPath)
	{
		_ = fileOutputPath ?? throw new ArgumentNullException(nameof(fileOutputPath));
		var bytes = Screenshot(GetImageFormatFromPath(fileOutputPath));
		var directory = Path.GetDirectoryName(Path.GetFullPath(fileOutputPath));
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);
		File.WriteAllBytes(fileOutputPath, bytes);
		return this;
	}

	public Element Screenshot(out byte[] screenshotBytes, ImageFormat format = ImageFormat.Jpeg)
	{
		screenshotBytes = Screenshot(format);
		return this;
	}

	public Element SelectText(string text) =>
		SetProperty("SelectedText", text);

	public Element RaiseEvent(string eventName) =>
		SendTargetedWithRepair(() => new RaiseEventCommandRequest { TargetId = TargetId, EventName = eventName });

	public Element RaiseEvent<TInput>(Expression<Func<TInput, RoutedEventArgs>> code) =>
		SendTargetedWithRepair(() => new RaiseEventCommandRequest { TargetId = TargetId, GetRoutedEventArgs = ExpressionPayloadSerializer.Serialize(code) });

	public Element Invoke(string methodName, bool allowUnsafeCode = false) =>
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = methodName, AllowUnsafeCode = allowUnsafeCode });

	public Element Invoke<TInput>(Expression<Action<TInput>> code) =>
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = ExpressionPayloadSerializer.Serialize(code), AllowUnsafeCode = true });

	public Element SetProperty(string propertyName, object? value) =>
		SendTargetedWithRepair(() => new SetPropertyCommandRequest { TargetId = TargetId, PropertyName = propertyName, PropertyValue = value });

	public Element SetProperty<TInput, TOutput>(string propertyName, Expression<Func<TInput, TOutput>> getValue) =>
		SendTargetedWithRepair(() => new SetPropertyCommandRequest
		{
			TargetId = TargetId,
			PropertyName = propertyName,
			PropertyValue = ExpressionPayloadSerializer.Serialize(getValue),
		});

	internal static Element FromMatch(AppDriver driver, FindElementMatchResponse match, ElementSelector? selector)
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
			selector);
	}

	internal static Element FromNode(AppDriver driver, VisualTreeNodeDto node, VisualTreeSnapshot snapshot) =>
		new(driver, node, snapshot: snapshot);

	protected void ReplaceNode(VisualTreeNodeDto replacement)
	{
		node = replacement;
		Snapshot = null;
	}

	private Element KnownOperation(string operation) =>
		SendTargetedWithRepair(() => new KnownOperationCommandRequest { TargetId = TargetId, Operation = operation });

	private Element SendTargetedWithRepair(Func<IpcCommand> commandFactory)
	{
		var response = SendWithRepair<StandardIpcResponse>(commandFactory);
		if (response.Success != true)
			throw new AppDriverException(response.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, response.Error ?? "Command failed.");
		return this;
	}

	private TResponse SendWithRepair<TResponse>(Func<IpcCommand> commandFactory)
	{
		var response = driver.Send<TResponse>(commandFactory());
		if (IsFailure(response, ProtocolConstants.ErrorCodes.StaleTarget))
		{
			var repaired = driver.Repair(this);
			ReplaceNode(repaired.node);
			response = driver.Send<TResponse>(commandFactory());
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
}

public class Element<TNative> : Element
{
	public Element(Element source)
		: base(source)
	{
	}

	internal Element(AppDriver driver, VisualTreeNodeDto node, ElementSelector? selector = null, VisualTreeSnapshot? snapshot = null)
		: base(driver, node, selector, snapshot)
	{
	}
}
