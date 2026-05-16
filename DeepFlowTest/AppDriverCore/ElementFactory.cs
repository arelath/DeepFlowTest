namespace DeepFlowTest;

using System;
using System.Reflection;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal sealed class ElementFactory
{
	private readonly AppDriver driver;

	public ElementFactory(AppDriver driver)
	{
		this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
	}

	public Element FromMatch(
		FindElementMatchResponse match,
		ElementSelector? selector,
		ElementRepairInfo? repairInfo = null) =>
		Element.FromMatch(driver, match, selector, repairInfo);

	public Element FromNode(
		VisualTreeNodeDto node,
		VisualTreeSnapshot snapshot,
		ElementRepairInfo? repairInfo = null,
		bool register = true) =>
		Element.FromNode(driver, node, snapshot, repairInfo, register);

	public Element FromSnapshot(VisualTreeNodeDto node, VisualTreeSnapshot snapshot) =>
		Element.FromSnapshot(node, snapshot);

	public TElement Wrap<TElement>(Element element)
		where TElement : Element
	{
		if (element is TElement typed)
			return typed;

		var constructor = typeof(TElement).GetConstructor(
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			binder: null,
			types: new[] { typeof(Element) },
			modifiers: null);
		if (constructor is not null)
			return (TElement)constructor.Invoke(new object[] { element });

		throw new AppDriverException(AppDriverErrorCodes.UnsupportedTarget, $"Element type '{typeof(TElement).FullName}' must expose a constructor that accepts Element.");
	}
}
