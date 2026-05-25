namespace DeepFlowTest.AppDriverPayload.Diagnostics;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Shared;

internal sealed class WpfOverlayVirtualPointerRenderer : IVirtualPointerRenderer
{
	private readonly Dispatcher dispatcher;
	private readonly List<Point> dragPoints = [];
	private VirtualPointerOptionsDto options;
	private VirtualPointerWindow? window;
	private DispatcherTimer? hideTimer;

	public WpfOverlayVirtualPointerRenderer(Dispatcher dispatcher, VirtualPointerOptionsDto options)
	{
		this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		this.options = Clone(options);
	}

	public void Configure(VirtualPointerOptionsDto newOptions) =>
		RunOnDispatcher(() =>
		{
			options = Clone(newOptions);
			if (!options.Enabled)
			{
				window?.Hide();
				return;
			}

			if (hideTimer is not null)
				hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, options.HideDelayMs));
		});

	public void MoveTo(Point screenDevicePoint, IntPtr ownerHwnd) =>
		RunOnDispatcher(() =>
		{
			var activeWindow = EnsureWindow();
			activeWindow.ShowAt(screenDevicePoint);
			RestartHideTimer();
		});

	public void Click(MouseButtonKind button, int clickCount) =>
		RunOnDispatcher(() =>
		{
			if (!options.ShowClickRipples || window is null)
				return;

			window.ShowClickRipple(Math.Max(1, clickCount));
			RestartHideTimer();
		});

	public void BeginDrag(Point screenDevicePoint, IntPtr ownerHwnd) =>
		RunOnDispatcher(() =>
		{
			dragPoints.Clear();
			dragPoints.Add(screenDevicePoint);
			var activeWindow = EnsureWindow();
			activeWindow.ClearDragTrail();
			activeWindow.ShowAt(screenDevicePoint);
			RestartHideTimer();
		});

	public void DragMove(Point screenDevicePoint) =>
		RunOnDispatcher(() =>
		{
			dragPoints.Add(screenDevicePoint);
			window?.ShowAt(screenDevicePoint);
			if (options.ShowDragTrail)
				window?.ShowDragTrail(dragPoints);
			RestartHideTimer();
		});

	public void EndDrag(Point screenDevicePoint) =>
		RunOnDispatcher(() =>
		{
			dragPoints.Add(screenDevicePoint);
			window?.ShowAt(screenDevicePoint);
			if (options.ShowDragTrail)
				window?.ShowDragTrail(dragPoints);
			window?.ShowClickRipple(1);
			RestartHideTimer();
		});

	public void Hide() =>
		RunOnDispatcher(() =>
		{
			hideTimer?.Stop();
			window?.Hide();
			dragPoints.Clear();
		});

	public void Dispose() =>
		RunOnDispatcher(() =>
		{
			hideTimer?.Stop();
			hideTimer = null;
			window?.Close();
			window = null;
			dragPoints.Clear();
		});

	private VirtualPointerWindow EnsureWindow()
	{
		if (window is not null)
			return window;

		window = new VirtualPointerWindow();
		return window;
	}

	private void RestartHideTimer()
	{
		if (options.HideDelayMs <= 0)
			return;

		hideTimer ??= new DispatcherTimer(DispatcherPriority.Background, dispatcher)
		{
			Interval = TimeSpan.FromMilliseconds(Math.Max(1, options.HideDelayMs)),
		};
		hideTimer.Tick -= HideTimerOnTick;
		hideTimer.Tick += HideTimerOnTick;
		hideTimer.Stop();
		hideTimer.Start();
	}

	private void HideTimerOnTick(object? sender, EventArgs e)
	{
		hideTimer?.Stop();
		window?.Hide();
		dragPoints.Clear();
	}

	private void RunOnDispatcher(Action action)
	{
		if (dispatcher.CheckAccess())
		{
			action();
			return;
		}

		dispatcher.BeginInvoke(action);
	}

	private static VirtualPointerOptionsDto Clone(VirtualPointerOptionsDto source) =>
		new()
		{
			Enabled = source.Enabled,
			ShowClickRipples = source.ShowClickRipples,
			ShowDragTrail = source.ShowDragTrail,
			HideDelayMs = source.HideDelayMs,
			IncludeInScreenshots = source.IncludeInScreenshots,
		};
}

internal sealed class VirtualPointerWindow : Window
{
	private readonly VirtualPointerRoot root = new();
	private readonly Polygon pointer;
	private readonly Ellipse ripple;
	private readonly Polyline trail;
	private Point lastLocalPoint;

	public VirtualPointerWindow()
	{
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		ShowInTaskbar = false;
		ShowActivated = false;
		Focusable = false;
		Topmost = true;
		Background = Brushes.Transparent;
		IsHitTestVisible = false;
		SizeToContent = SizeToContent.Manual;
		Content = root;

		trail = new Polyline
		{
			Stroke = new SolidColorBrush(Color.FromArgb(180, 0, 135, 212)),
			StrokeThickness = 3,
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
			IsHitTestVisible = false,
		};
		root.Children.Add(trail);

		ripple = new Ellipse
		{
			Width = 4,
			Height = 4,
			Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 80, 80)),
			StrokeThickness = 2,
			Opacity = 0,
			IsHitTestVisible = false,
		};
		root.Children.Add(ripple);

		pointer = new Polygon
		{
			Points = new PointCollection
			{
				new(0, 0),
				new(0, 26),
				new(7, 19),
				new(12, 31),
				new(17, 29),
				new(12, 17),
				new(22, 17),
			},
			Fill = Brushes.White,
			Stroke = new SolidColorBrush(Color.FromRgb(0, 89, 130)),
			StrokeThickness = 1.5,
			Effect = new System.Windows.Media.Effects.DropShadowEffect
			{
				Color = Colors.Black,
				BlurRadius = 4,
				ShadowDepth = 1,
				Opacity = 0.45,
			},
			IsHitTestVisible = false,
		};
		root.Children.Add(pointer);
	}

	public void ShowAt(Point screenDevicePoint)
	{
		EnsureOverlayBounds();
		if (!IsVisible)
			Show();

		lastLocalPoint = PointFromScreen(screenDevicePoint);
		Canvas.SetLeft(pointer, lastLocalPoint.X);
		Canvas.SetTop(pointer, lastLocalPoint.Y);
	}

	public void ShowClickRipple(int clickCount)
	{
		var size = clickCount > 1 ? 46 : 34;
		var fromSize = 4.0;
		Canvas.SetLeft(ripple, lastLocalPoint.X - fromSize / 2);
		Canvas.SetTop(ripple, lastLocalPoint.Y - fromSize / 2);
		ripple.Width = fromSize;
		ripple.Height = fromSize;
		ripple.Opacity = 0.85;

		var duration = TimeSpan.FromMilliseconds(220);
		var widthAnimation = new DoubleAnimation(size, duration) { EasingFunction = new QuadraticEase() };
		var heightAnimation = new DoubleAnimation(size, duration) { EasingFunction = new QuadraticEase() };
		var leftAnimation = new DoubleAnimation(lastLocalPoint.X - size / 2, duration) { EasingFunction = new QuadraticEase() };
		var topAnimation = new DoubleAnimation(lastLocalPoint.Y - size / 2, duration) { EasingFunction = new QuadraticEase() };
		var opacityAnimation = new DoubleAnimation(0, duration);
		ripple.BeginAnimation(WidthProperty, widthAnimation);
		ripple.BeginAnimation(HeightProperty, heightAnimation);
		ripple.BeginAnimation(Canvas.LeftProperty, leftAnimation);
		ripple.BeginAnimation(Canvas.TopProperty, topAnimation);
		ripple.BeginAnimation(OpacityProperty, opacityAnimation);
	}

	public void ShowDragTrail(IReadOnlyList<Point> screenDevicePoints)
	{
		trail.Points.Clear();
		foreach (var point in screenDevicePoints)
			trail.Points.Add(PointFromScreen(point));
	}

	public void ClearDragTrail() =>
		trail.Points.Clear();

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		var handle = new WindowInteropHelper(this).Handle;
		if (handle == IntPtr.Zero)
			return;

		var styles = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE);
		var nextStyles = new IntPtr(styles.ToInt64()
			| NativeMethods.WS_EX_TRANSPARENT
			| NativeMethods.WS_EX_NOACTIVATE
			| NativeMethods.WS_EX_TOOLWINDOW);
		NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, nextStyles);
	}

	private void EnsureOverlayBounds()
	{
		Left = SystemParameters.VirtualScreenLeft;
		Top = SystemParameters.VirtualScreenTop;
		Width = SystemParameters.VirtualScreenWidth;
		Height = SystemParameters.VirtualScreenHeight;
	}
}

internal sealed class VirtualPointerRoot : Canvas
{
	public VirtualPointerRoot()
	{
		Background = Brushes.Transparent;
		IsHitTestVisible = false;
	}
}
