namespace DeepFlowTest.Utility.WpfUtility.SelectionHighlight;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

internal static class UIHighlight
{
	private static IDisposable? currentHighlight;

	public static void Select(DependencyObject dependencyObject)
	{
		try
		{
			currentHighlight?.Dispose();

			var uiElement = FindUIElement(dependencyObject);
			if (uiElement is null)
				return;

			currentHighlight = CreateAndAttachSelectionHighlightAdorner(uiElement);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
		}
	}

	private static IDisposable? CreateAndAttachSelectionHighlightAdorner(UIElement uiElement)
	{
		var adornerLayer = AdornerLayer.GetAdornerLayer(uiElement);
		if (adornerLayer is null)
			return null;

		var selectionAdorner = new SelectionAdorner(uiElement)
		{
			AdornerLayer = adornerLayer,
		};

		adornerLayer.Add(selectionAdorner);
		return selectionAdorner;
	}

	private static UIElement? FindUIElement(DependencyObject dependencyObject)
	{
		return dependencyObject switch
		{
			UIElement uiElement => uiElement,
			ColumnDefinition columnDefinition => columnDefinition.Parent as UIElement,
			RowDefinition rowDefinition => rowDefinition.Parent as UIElement,
			ContentElement contentElement => LogicalTreeHelper.GetParent(contentElement) as UIElement,
			_ => null,
		};
	}
}
