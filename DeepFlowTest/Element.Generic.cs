namespace DeepFlowTest;

using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

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
	public override T DragAndDropTo(Element destination, DragAndDropOptions? options = null) => Return(base.DragAndDropTo(destination, options));
	public override T DragAndDropTo(ElementSelector destinationSelector, DragAndDropOptions? options = null) => Return(base.DragAndDropTo(destinationSelector, options));
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
	public override T Invoke<TInput>(Expression<Action<TInput>> code, TimeSpan? timeout = null) => Return(base.Invoke(code, timeout));
	public override T Invoke<TInput, TOutput>(Expression<Func<TInput, TOutput>> code, out TOutput result, TimeSpan? timeout = null)
	{
		var returned = base.Invoke(code, out TOutput? value, timeout);
		result = value!;
		return Return(returned);
	}
	public override T InvokeAsync<TInput>(Expression<Func<TInput, Task>> code, TimeSpan? timeout = null) => Return(base.InvokeAsync(code, timeout));
	public override T InvokeAsync<TInput, TOutput>(Expression<Func<TInput, Task<TOutput>>> code, out TOutput result, TimeSpan? timeout = null)
	{
		var returned = base.InvokeAsync(code, out TOutput? value, timeout);
		result = value!;
		return Return(returned);
	}
	public override T SetProperty(string propertyName, object? value) => Return(base.SetProperty(propertyName, value));
	public override T SetProperty<TInput, TOutput>(string propertyName, Expression<Func<TInput, TOutput>> getValue) => Return(base.SetProperty(propertyName, getValue));
	public override T Assert(Expression<Func<Element, bool?>> predicateExpression, TimeSpan? timeout = null) => Return(base.Assert(predicateExpression, timeout));

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
	public new T DragAndDropTo(Element destination, DragAndDropOptions? options = null) => Return(base.DragAndDropTo(destination, options));
	public new T DragAndDropTo(ElementSelector destinationSelector, DragAndDropOptions? options = null) => Return(base.DragAndDropTo(destinationSelector, options));
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
	public new T Invoke<TInput>(Expression<Action<TInput>> code, TimeSpan? timeout = null) => Return(base.Invoke(code, timeout));
	public new T Invoke<TInput, TOutput>(Expression<Func<TInput, TOutput>> code, out TOutput? result, TimeSpan? timeout = null) => Return(base.Invoke(code, out result, timeout));
	public new T InvokeAsync<TInput>(Expression<Func<TInput, Task>> code, TimeSpan? timeout = null) => Return(base.InvokeAsync(code, timeout));
	public new T InvokeAsync<TInput, TOutput>(Expression<Func<TInput, Task<TOutput>>> code, out TOutput? result, TimeSpan? timeout = null) => Return(base.InvokeAsync(code, out result, timeout));
	public new T SetProperty(string propertyName, object? value) => Return(base.SetProperty(propertyName, value));
	public new T SetProperty<TInput, TOutput>(string propertyName, Expression<Func<TInput, TOutput>> getValue) => Return(base.SetProperty(propertyName, getValue));
	public new T Assert(Expression<Func<Element, bool?>> predicateExpression, TimeSpan? timeout = null) => Return(base.Assert(predicateExpression, timeout));

	private T Return(Element _)
	{
		if (this is T typed)
			return typed;

		throw new InvalidCastException($"Element wrapper '{GetType().FullName}' cannot be returned as '{typeof(T).FullName}'.");
	}
}
#endif
