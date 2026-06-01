namespace DeepFlowTest.AppDriverPayload.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeepFlowTest.Contracts;

internal sealed class WpfAdornerVirtualPointerRenderer : IVirtualPointerRenderer
{
	private readonly Dispatcher dispatcher;
	private readonly List<Point> dragPoints = [];
	private VirtualPointerOptionsDto options;
	private DispatcherTimer? hideTimer;
	private DispatcherTimer? animationTimer;
	private AdornerLayer? adornerLayer;
	private UIElement? adornedElement;
	private VirtualPointerAdorner? adorner;
	private IntPtr ownerHwnd;

	public WpfAdornerVirtualPointerRenderer(Dispatcher dispatcher, VirtualPointerOptionsDto options)
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
				Hide();
				return;
			}

			if (hideTimer is not null)
				hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, options.HideDelayMs));
		});

	public void MoveTo(Point screenDevicePoint, IntPtr ownerHwnd) =>
		RunOnDispatcher(() =>
		{
			if (!TryAttach(ownerHwnd))
				return;

			adorner?.MoveTo(ToAdornerPoint(screenDevicePoint));
			RestartHideTimer();
		});

	public void Click(MouseButtonKind button, int clickCount) =>
		RunOnDispatcher(() =>
		{
			if (!options.ShowClickRipples || adorner is null)
				return;

			adorner.ShowClickRipple(Math.Max(1, clickCount));
			StartAnimationTimer();
			RestartHideTimer();
		});

	public void BeginDrag(Point screenDevicePoint, IntPtr ownerHwnd) =>
		RunOnDispatcher(() =>
		{
			dragPoints.Clear();
			dragPoints.Add(screenDevicePoint);
			if (!TryAttach(ownerHwnd))
				return;

			adorner?.ClearDragTrail();
			adorner?.MoveTo(ToAdornerPoint(screenDevicePoint));
			RestartHideTimer();
		});

	public void DragMove(Point screenDevicePoint) =>
		RunOnDispatcher(() =>
		{
			if (adorner is null)
				return;

			dragPoints.Add(screenDevicePoint);
			adorner.MoveTo(ToAdornerPoint(screenDevicePoint));
			if (options.ShowDragTrail)
				adorner.ShowDragTrail(dragPoints.Select(ToAdornerPoint));
			RestartHideTimer();
		});

	public void EndDrag(Point screenDevicePoint) =>
		RunOnDispatcher(() =>
		{
			if (adorner is null)
				return;

			dragPoints.Add(screenDevicePoint);
			adorner.MoveTo(ToAdornerPoint(screenDevicePoint));
			if (options.ShowDragTrail)
				adorner.ShowDragTrail(dragPoints.Select(ToAdornerPoint));
			if (options.ShowClickRipples)
			{
				adorner.ShowClickRipple(1);
				StartAnimationTimer();
			}

			RestartHideTimer();
		});

	public void Hide() =>
		RunOnDispatcher(() =>
		{
			hideTimer?.Stop();
			adorner?.HidePointer();
			dragPoints.Clear();
		});

	public void Dispose() =>
		RunOnDispatcher(() =>
		{
			hideTimer?.Stop();
			hideTimer = null;
			animationTimer?.Stop();
			animationTimer = null;
			Detach();
			dragPoints.Clear();
		});

	private bool TryAttach(IntPtr requestedOwnerHwnd)
	{
		if (adorner is not null && requestedOwnerHwnd == ownerHwnd)
			return true;

		Detach();

		var target = ResolveAdornerTarget(requestedOwnerHwnd);
		if (target is null)
			return false;

		var layer = AdornerLayer.GetAdornerLayer(target);
		if (layer is null)
			return false;

		adorner = new VirtualPointerAdorner(target);
		layer.Add(adorner);
		adornerLayer = layer;
		adornedElement = target;
		ownerHwnd = requestedOwnerHwnd;
		return true;
	}

	private Point ToAdornerPoint(Point screenDevicePoint) =>
		adornedElement?.PointFromScreen(screenDevicePoint) ?? screenDevicePoint;

	private void Detach()
	{
		if (adorner is not null && adornerLayer is not null)
			adornerLayer.Remove(adorner);

		adorner = null;
		adornerLayer = null;
		adornedElement = null;
		ownerHwnd = IntPtr.Zero;
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

	private void StartAnimationTimer()
	{
		animationTimer ??= new DispatcherTimer(DispatcherPriority.Background, dispatcher)
		{
			Interval = TimeSpan.FromMilliseconds(16),
		};
		animationTimer.Tick -= AnimationTimerOnTick;
		animationTimer.Tick += AnimationTimerOnTick;
		animationTimer.Start();
	}

	private void AnimationTimerOnTick(object? sender, EventArgs e)
	{
		if (adorner is null)
		{
			animationTimer?.Stop();
			return;
		}

		adorner.InvalidateVisual();
		if (!adorner.HasActiveRipples)
			animationTimer?.Stop();
	}

	private void HideTimerOnTick(object? sender, EventArgs e)
	{
		hideTimer?.Stop();
		adorner?.HidePointer();
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

	private static UIElement? ResolveAdornerTarget(IntPtr requestedOwnerHwnd)
	{
		var root = requestedOwnerHwnd != IntPtr.Zero
			? HwndSource.FromHwnd(requestedOwnerHwnd)?.RootVisual as DependencyObject
			: null;

		if (root is not null)
			return GetAdornerTarget(root);
		if (requestedOwnerHwnd != IntPtr.Zero)
			return null;

		if (Application.Current is null)
			return null;

		foreach (Window window in Application.Current.Windows)
		{
			if (window.IsVisible && GetAdornerTarget(window) is { } target)
				return target;
		}

		return null;
	}

	private static UIElement? GetAdornerTarget(DependencyObject root)
	{
		if (root is Window { Content: UIElement content })
			return content;

		return root as UIElement;
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

internal sealed class VirtualPointerAdorner : Adorner
{
	private static readonly Brush PointerFill = Brushes.White;
	private static readonly Pen PointerStroke = new(new SolidColorBrush(Color.FromRgb(0, 89, 130)), 1.5);
	private static readonly Pen TrailPen = new(new SolidColorBrush(Color.FromArgb(180, 0, 135, 212)), 3);
	private readonly List<Point> dragTrail = [];
	private readonly List<Ripple> ripples = [];
	private Point pointerPoint;
	private bool visible;

	static VirtualPointerAdorner()
	{
		IsHitTestVisibleProperty.OverrideMetadata(typeof(VirtualPointerAdorner), new UIPropertyMetadata(false));
		UseLayoutRoundingProperty.OverrideMetadata(typeof(VirtualPointerAdorner), new FrameworkPropertyMetadata(true));
	}

	public VirtualPointerAdorner(UIElement adornedElement)
		: base(adornedElement)
	{
	}

	public bool HasActiveRipples
	{
		get
		{
			PruneCompletedRipples();
			return ripples.Count != 0;
		}
	}

	public void MoveTo(Point localPoint)
	{
		pointerPoint = localPoint;
		visible = true;
		InvalidateVisual();
	}

	public void ShowClickRipple(int clickCount)
	{
		visible = true;
		ripples.Add(new Ripple(pointerPoint, DateTimeOffset.UtcNow, clickCount > 1 ? 46 : 34));
		InvalidateVisual();
	}

	public void ShowDragTrail(IEnumerable<Point> points)
	{
		dragTrail.Clear();
		dragTrail.AddRange(points);
		InvalidateVisual();
	}

	public void ClearDragTrail()
	{
		dragTrail.Clear();
		InvalidateVisual();
	}

	public void HidePointer()
	{
		visible = false;
		dragTrail.Clear();
		ripples.Clear();
		InvalidateVisual();
	}

	protected override void OnRender(DrawingContext drawingContext)
	{
		if (!visible)
			return;

		var bounds = new Rect(AdornedElement.RenderSize);
		drawingContext.PushClip(new RectangleGeometry(bounds));
		DrawDragTrail(drawingContext);
		DrawRipples(drawingContext);
		DrawPointer(drawingContext);
		drawingContext.Pop();
	}

	private void DrawDragTrail(DrawingContext drawingContext)
	{
		if (dragTrail.Count < 2)
			return;

		var geometry = new StreamGeometry();
		using (var context = geometry.Open())
		{
			context.BeginFigure(dragTrail[0], isFilled: false, isClosed: false);
			for (var i = 1; i < dragTrail.Count; i++)
				context.LineTo(dragTrail[i], isStroked: true, isSmoothJoin: true);
		}

		drawingContext.DrawGeometry(null, TrailPen, geometry);
	}

	private void DrawRipples(DrawingContext drawingContext)
	{
		PruneCompletedRipples();
		foreach (var ripple in ripples)
		{
			var progress = Math.Min(1, (DateTimeOffset.UtcNow - ripple.StartedAt).TotalMilliseconds / Ripple.DurationMs);
			var eased = 1 - Math.Pow(1 - progress, 2);
			var diameter = 4 + (ripple.TargetDiameter - 4) * eased;
			var alpha = (byte)Math.Round(220 * (1 - progress));
			if (alpha == 0)
				continue;

			var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 255, 80, 80)), 2);
			drawingContext.DrawEllipse(null, pen, ripple.Center, diameter / 2, diameter / 2);
		}
	}

	private void DrawPointer(DrawingContext drawingContext)
	{
		var geometry = new StreamGeometry();
		using (var context = geometry.Open())
		{
			context.BeginFigure(pointerPoint, isFilled: true, isClosed: true);
			context.LineTo(Offset(0, 26), isStroked: true, isSmoothJoin: false);
			context.LineTo(Offset(7, 19), isStroked: true, isSmoothJoin: false);
			context.LineTo(Offset(12, 31), isStroked: true, isSmoothJoin: false);
			context.LineTo(Offset(17, 29), isStroked: true, isSmoothJoin: false);
			context.LineTo(Offset(12, 17), isStroked: true, isSmoothJoin: false);
			context.LineTo(Offset(22, 17), isStroked: true, isSmoothJoin: false);
		}

		drawingContext.DrawGeometry(PointerFill, PointerStroke, geometry);
	}

	private Point Offset(double x, double y) =>
		new(pointerPoint.X + x, pointerPoint.Y + y);

	private void PruneCompletedRipples()
	{
		var now = DateTimeOffset.UtcNow;
		ripples.RemoveAll(ripple => (now - ripple.StartedAt).TotalMilliseconds >= Ripple.DurationMs);
	}

	private readonly struct Ripple
	{
		public const double DurationMs = 220;

		public Ripple(Point center, DateTimeOffset startedAt, double targetDiameter)
		{
			Center = center;
			StartedAt = startedAt;
			TargetDiameter = targetDiameter;
		}

		public Point Center { get; }

		public DateTimeOffset StartedAt { get; }

		public double TargetDiameter { get; }
	}
}
