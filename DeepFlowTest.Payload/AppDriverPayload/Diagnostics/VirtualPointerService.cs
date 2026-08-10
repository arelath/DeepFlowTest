namespace DeepFlowTest.AppDriverPayload.Diagnostics;

using System;
using System.Collections.Generic;
using System.Windows;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility;

internal interface IVirtualPointerRenderer : IDisposable
{
	void Configure(VirtualPointerOptionsDto options);

	void MoveTo(Point screenDevicePoint, IntPtr ownerHwnd);

	void Click(MouseButtonKind button, int clickCount);

	void BeginDrag(Point screenDevicePoint, IntPtr ownerHwnd);

	void DragMove(Point screenDevicePoint);

	void EndDrag(Point screenDevicePoint);

	void Hide();
}

internal static class VirtualPointerService
{
	private static readonly object Gate = new();
	private static VirtualPointerOptionsDto options = new();
	private static IVirtualPointerRenderer? renderer;
	private static bool rendererCreationFailed;
	private static Func<VirtualPointerOptionsDto, IVirtualPointerRenderer?> rendererFactory = CreateDefaultRenderer;

	public static void Configure(VirtualPointerOptionsDto newOptions)
	{
		_ = newOptions ?? throw new ArgumentNullException(nameof(newOptions));

		lock (Gate)
		{
			options = Clone(newOptions);
			rendererCreationFailed = false;
			if (!options.Enabled)
			{
				DisposeRenderer();
				return;
			}

			try
			{
				renderer?.Configure(Clone(options));
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				PayloadLog.Write("Virtual pointer renderer configuration failed.", ex);
				DisposeRenderer();
				rendererCreationFailed = true;
			}
		}
	}

	public static void MoveTo(Point screenDevicePoint, IntPtr ownerHwnd = default) =>
		WithRenderer(renderer => renderer.MoveTo(screenDevicePoint, ownerHwnd));

	public static void Click(MouseButtonKind button, int clickCount = 1) =>
		WithRenderer(renderer => renderer.Click(button, clickCount));

	public static void BeginDrag(Point screenDevicePoint, IntPtr ownerHwnd = default) =>
		WithRenderer(renderer => renderer.BeginDrag(screenDevicePoint, ownerHwnd));

	public static void DragMove(Point screenDevicePoint) =>
		WithRenderer(renderer => renderer.DragMove(screenDevicePoint));

	public static void EndDrag(Point screenDevicePoint) =>
		WithRenderer(renderer => renderer.EndDrag(screenDevicePoint));

	public static void Hide() =>
		WithRenderer(static renderer => renderer.Hide());

	internal static IDisposable UseRendererFactoryForTests(Func<VirtualPointerOptionsDto, IVirtualPointerRenderer?> factory)
	{
		_ = factory ?? throw new ArgumentNullException(nameof(factory));

		lock (Gate)
		{
			var previousFactory = rendererFactory;
			DisposeRenderer();
			rendererFactory = factory;
			rendererCreationFailed = false;
			return new RestoreRendererFactory(previousFactory);
		}
	}

	internal static void ResetForTests()
	{
		lock (Gate)
		{
			options = new VirtualPointerOptionsDto();
			rendererCreationFailed = false;
			DisposeRenderer();
			rendererFactory = CreateDefaultRenderer;
		}
	}

	private static void WithRenderer(Action<IVirtualPointerRenderer> action)
	{
		_ = action ?? throw new ArgumentNullException(nameof(action));

		lock (Gate)
		{
			if (!options.Enabled)
				return;

			var activeRenderer = GetOrCreateRenderer();
			if (activeRenderer is null)
				return;

			try
			{
				action(activeRenderer);
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				PayloadLog.Write("Virtual pointer renderer operation failed.", ex);
				DisposeRenderer();
				rendererCreationFailed = true;
			}
		}
	}

	private static IVirtualPointerRenderer? GetOrCreateRenderer()
	{
		if (renderer is not null)
			return renderer;
		if (rendererCreationFailed)
			return null;

		try
		{
			renderer = rendererFactory(Clone(options));
			renderer?.Configure(Clone(options));
			return renderer;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			PayloadLog.Write("Virtual pointer renderer creation failed.", ex);
			DisposeRenderer();
			rendererCreationFailed = true;
			return null;
		}
	}

	private static IVirtualPointerRenderer? CreateDefaultRenderer(VirtualPointerOptionsDto currentOptions)
	{
		if (!currentOptions.Enabled)
			return null;

		var dispatcher = ThreadUtility.FindWpfDispatcher();
		return dispatcher is null ? null : new WpfAdornerVirtualPointerRenderer(dispatcher, currentOptions);
	}

	private static void DisposeRenderer()
	{
		try
		{
			renderer?.Dispose();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			PayloadLog.Write("Virtual pointer renderer disposal failed.", ex);
		}
		finally
		{
			renderer = null;
		}
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

	private sealed class RestoreRendererFactory : IDisposable
	{
		private readonly Func<VirtualPointerOptionsDto, IVirtualPointerRenderer?> previousFactory;
		private bool disposed;

		public RestoreRendererFactory(Func<VirtualPointerOptionsDto, IVirtualPointerRenderer?> previousFactory)
		{
			this.previousFactory = previousFactory;
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			lock (Gate)
			{
				DisposeRenderer();
				rendererFactory = previousFactory;
				rendererCreationFailed = false;
				options = new VirtualPointerOptionsDto();
			}
		}
	}
}
