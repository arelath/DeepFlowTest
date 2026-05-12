namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
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

	public T? GetProperty<T>(string propertyName)
	{
		if (!Properties.TryGetValue(propertyName, out var value) || value is null)
			return default;

		if (value is T typed)
			return typed;

		return (T?)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
	}

	public void Click() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId });

	public void RightClick() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId, MouseButton = "right" });

	public void DoubleClick() => SendTargetedWithRepair(() => new ClickCommandRequest { TargetId = TargetId, ClickCount = 2 });

	public void Focus() => SendTargetedWithRepair(() => new FocusCommandRequest { TargetId = TargetId });

	public void Select() => KnownOperation("Select");

	public void Expand() => KnownOperation("Expand");

	public void Collapse() => KnownOperation("Collapse");

	public void Check() => KnownOperation("Check");

	public void Uncheck() => KnownOperation("Uncheck");

	public void ScrollIntoView() => KnownOperation("BringIntoView");

	public void AcceptDialog() => KnownOperation("AcceptDialog");

	public void CancelDialog() => KnownOperation("CancelDialog");

	public void Type(string text, bool clearFirst = false)
	{
		SendTargetedWithRepair(() => new TypeTextCommandRequest { TargetId = TargetId, Text = text, ClearFirst = clearFirst });
	}

	public ScreenshotCommandResponse Screenshot(string format = "png") =>
		SendWithRepair<ScreenshotCommandResponse>(() => new ScreenshotCommandRequest { TargetId = TargetId, Format = format });

	public void RaiseEvent(string eventName) =>
		SendTargetedWithRepair(() => new RaiseEventCommandRequest { TargetId = TargetId, EventName = eventName });

	public void Invoke(string methodName, bool allowUnsafeCode = false) =>
		SendTargetedWithRepair(() => new InvokeCommandRequest { TargetId = TargetId, Code = methodName, AllowUnsafeCode = allowUnsafeCode });

	public void SetProperty(string propertyName, object? value) =>
		SendTargetedWithRepair(() => new SetPropertyCommandRequest { TargetId = TargetId, PropertyName = propertyName, PropertyValue = value });

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

	private void KnownOperation(string operation) =>
		SendTargetedWithRepair(() => new KnownOperationCommandRequest { TargetId = TargetId, Operation = operation });

	private void SendTargetedWithRepair(Func<IpcCommand> commandFactory)
	{
		var response = SendWithRepair<StandardIpcResponse>(commandFactory);
		if (response.Success != true)
			throw new AppDriverException(response.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, response.Error ?? "Command failed.");
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
}

public sealed class Element<TNative> : Element
{
	internal Element(AppDriver driver, VisualTreeNodeDto node, ElementSelector? selector = null, VisualTreeSnapshot? snapshot = null)
		: base(driver, node, selector, snapshot)
	{
	}
}
