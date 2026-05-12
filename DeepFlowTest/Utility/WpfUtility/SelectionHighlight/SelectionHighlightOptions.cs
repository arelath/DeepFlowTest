namespace DeepFlowTest.Utility.WpfUtility.SelectionHighlight;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

internal sealed class SelectionHighlightOptions : INotifyPropertyChanged
{
	private Brush? background;
	private Brush borderBrush = null!;
	private double borderThickness;
	private bool highlightSelectedItem = true;
	private Pen? pen;

	public event PropertyChangedEventHandler? PropertyChanged;

	public static SelectionHighlightOptions Default { get; } = new();

	public SelectionHighlightOptions()
	{
		Reset();
	}

	public Brush? Background
	{
		get => background;
		set
		{
			if (Equals(value, background))
				return;

			background = value;
			OnPropertyChanged();
		}
	}

	public Brush BorderBrush
	{
		get => borderBrush;
		set
		{
			if (Equals(value, borderBrush))
				return;

			pen = null;
			borderBrush = value;
			OnPropertyChanged();
		}
	}

	public double BorderThickness
	{
		get => borderThickness;
		set
		{
			if (value.Equals(borderThickness))
				return;

			pen = null;
			borderThickness = value;
			OnPropertyChanged();
		}
	}

	public bool HighlightSelectedItem
	{
		get => highlightSelectedItem;
		set
		{
			if (value == highlightSelectedItem)
				return;

			highlightSelectedItem = value;
			OnPropertyChanged();
		}
	}

	public Pen Pen => pen ??= new Pen(BorderBrush, BorderThickness);

	public void Reset()
	{
		Background = null;
		BorderThickness = 3D;
		var borderColor = new Color
		{
			ScA = .3f,
			ScR = 1,
		};
		BorderBrush = new SolidColorBrush(borderColor);
		BorderBrush.Freeze();
	}

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
