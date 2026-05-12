namespace DeepFlowTest.Utility.WpfUtility.SelectionHighlight;

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

internal sealed class SelectionAdorner : Adorner, IDisposable
{
	static SelectionAdorner()
	{
		IsHitTestVisibleProperty.OverrideMetadata(typeof(SelectionAdorner), new UIPropertyMetadata(false));
		UseLayoutRoundingProperty.OverrideMetadata(typeof(SelectionAdorner), new FrameworkPropertyMetadata(true));
	}

	public SelectionAdorner(UIElement adornedElement)
		: base(adornedElement)
	{
		SelectionHighlightOptions.Default.PropertyChanged += SelectionHighlightOptionsOnPropertyChanged;
	}

	public AdornerLayer? AdornerLayer { get; set; }

	protected override void OnRender(DrawingContext drawingContext)
	{
		if (!SelectionHighlightOptions.Default.HighlightSelectedItem ||
			AreClose(ActualWidth, 0) ||
			AreClose(ActualHeight, 0))
		{
			return;
		}

		drawingContext.DrawRectangle(
			SelectionHighlightOptions.Default.Background,
			SelectionHighlightOptions.Default.Pen,
			new Rect(0, 0, ActualWidth, ActualHeight));
	}

	public void Dispose()
	{
		SelectionHighlightOptions.Default.PropertyChanged -= SelectionHighlightOptionsOnPropertyChanged;
		AdornerLayer?.Remove(this);
	}

	private void SelectionHighlightOptionsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		InvalidateVisual();
	}

	private static bool AreClose(double value1, double value2)
	{
		if (value1 == value2)
			return true;

		var epsilon = (Math.Abs(value1) + Math.Abs(value2) + 10.0) * 2.2204460492503131e-016;
		var delta = value1 - value2;
		return -epsilon < delta && epsilon > delta;
	}
}
