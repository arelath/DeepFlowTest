namespace DeepFlowTest.Utility.WpfUtility.Tree;

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DrawingImage = System.Drawing.Image;
using FormsControl = System.Windows.Forms.Control;

public abstract class TargetObjectWrapper : IDisposable
{
	private object? target;

	protected TargetObjectWrapper(object target, TargetObjectMetadata metadata)
	{
		this.target = target ?? throw new ArgumentNullException(nameof(target));
		Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
	}

	public TargetObjectMetadata Metadata { get; }

	public bool IsDisposed => target is null;

	public object Target => target ?? throw new ObjectDisposedException(GetType().Name);

	public bool TryGetTarget(out object? currentTarget)
	{
		currentTarget = target;
		return currentTarget is not null;
	}

	public static TargetObjectWrapper Create(object target)
	{
		_ = target ?? throw new ArgumentNullException(nameof(target));

		return target switch
		{
			SystemResourceRoot systemResourceRoot => new SystemResourceTargetObjectWrapper(systemResourceRoot),
			FormsControl control => new WinFormsTargetObjectWrapper(control),
			AutomationElement automationElement => new NativeAutomationElementTargetObjectWrapper(automationElement),
			IntPtr hwnd => new NativeWindowTargetObjectWrapper(hwnd),
			AutomationPeer automationPeer => new WpfAutomationPeerTargetObjectWrapper(automationPeer),
			ResourceDictionary resourceDictionary => new ResourceTargetObjectWrapper(resourceDictionary),
			ImageSource imageSource => new ImageTargetObjectWrapper(imageSource),
			DrawingImage drawingImage => new ImageTargetObjectWrapper(drawingImage),
			_ when IsKnownBrowserControl(target.GetType()) => new BrowserTargetObjectWrapper(target),
			DependencyObject dependencyObject => new WpfTargetObjectWrapper(dependencyObject),
			_ => new UnknownTargetObjectWrapper(target),
		};
	}

	public void Dispose()
	{
		target = null;
		GC.SuppressFinalize(this);
	}

	protected static TargetObjectMetadata CreateMetadata(
		object target,
		TargetObjectKind kind,
		string runtimeFamily,
		bool canReceiveActions,
		long? hwnd = null,
		string? displayTypeName = null,
		string? targetObjectType = null)
	{
		var type = target.GetType();
		return new TargetObjectMetadata
		{
			Kind = kind,
			TargetObjectType = targetObjectType ?? type.FullName ?? type.Name,
			DisplayTypeName = displayTypeName ?? type.Name,
			RuntimeFamily = runtimeFamily,
			Hwnd = hwnd,
			CanReceiveActions = canReceiveActions,
		};
	}

	private static bool IsKnownBrowserControl(Type type)
	{
		var fullName = type.FullName ?? string.Empty;
		return fullName == "System.Windows.Controls.WebBrowser"
			|| fullName == "Microsoft.Web.WebView2.Wpf.WebView2"
			|| fullName == "CefSharp.Wpf.ChromiumWebBrowser";
	}

	private sealed class WpfTargetObjectWrapper : TargetObjectWrapper
	{
		public WpfTargetObjectWrapper(DependencyObject target)
			: base(target, CreateWpfMetadata(target))
		{
		}

		private static TargetObjectMetadata CreateWpfMetadata(DependencyObject target)
		{
			var kind = target switch
			{
				Visual => TargetObjectKind.WpfVisual,
				Visual3D => TargetObjectKind.WpfVisual,
				ContentElement => TargetObjectKind.WpfLogicalObject,
				_ => TargetObjectKind.WpfDependencyObject,
			};

			return CreateMetadata(
				target,
				kind,
				"wpf",
				target is UIElement or ContentElement,
				TryGetWpfHwnd(target));
		}

		private static long? TryGetWpfHwnd(DependencyObject target)
		{
			if (target is Window window)
			{
				var windowHandle = new WindowInteropHelper(window).Handle;
				if (windowHandle != IntPtr.Zero)
					return windowHandle.ToInt64();
			}

			if (target is Visual visual && PresentationSource.FromVisual(visual) is HwndSource source && source.Handle != IntPtr.Zero)
				return source.Handle.ToInt64();

			return null;
		}
	}

	private sealed class WpfAutomationPeerTargetObjectWrapper : TargetObjectWrapper
	{
		public WpfAutomationPeerTargetObjectWrapper(AutomationPeer target)
			: base(target, CreateMetadata(target, TargetObjectKind.WpfAutomationPeer, "wpf", canReceiveActions: false))
		{
		}
	}

	private sealed class ResourceTargetObjectWrapper : TargetObjectWrapper
	{
		public ResourceTargetObjectWrapper(ResourceDictionary target)
			: base(target, CreateMetadata(target, TargetObjectKind.Resource, "wpf", canReceiveActions: false))
		{
		}
	}

	private sealed class SystemResourceTargetObjectWrapper : TargetObjectWrapper
	{
		public SystemResourceTargetObjectWrapper(SystemResourceRoot target)
			: base(target, CreateMetadata(target, TargetObjectKind.SystemResource, "wpf", canReceiveActions: false, displayTypeName: "SystemResources", targetObjectType: typeof(SystemResourceRoot).FullName))
		{
		}
	}

	private sealed class BrowserTargetObjectWrapper : TargetObjectWrapper
	{
		public BrowserTargetObjectWrapper(object target)
			: base(target, CreateMetadata(target, TargetObjectKind.WebBrowser, "browser", canReceiveActions: target is UIElement))
		{
		}
	}

	private sealed class ImageTargetObjectWrapper : TargetObjectWrapper
	{
		public ImageTargetObjectWrapper(object target)
			: base(target, CreateMetadata(target, TargetObjectKind.Image, "image", canReceiveActions: false))
		{
		}
	}

	private sealed class WinFormsTargetObjectWrapper : TargetObjectWrapper
	{
		public WinFormsTargetObjectWrapper(FormsControl target)
			: base(target, CreateMetadata(
				target,
				TargetObjectKind.WinFormsControl,
				"winforms",
				canReceiveActions: true,
				target.IsHandleCreated ? target.Handle.ToInt64() : null))
		{
		}
	}

	private sealed class NativeWindowTargetObjectWrapper : TargetObjectWrapper
	{
		public NativeWindowTargetObjectWrapper(IntPtr target)
			: base(target, CreateMetadata(
				target,
				TargetObjectKind.NativeWindow,
				"native",
				canReceiveActions: target != IntPtr.Zero,
				target == IntPtr.Zero ? null : target.ToInt64(),
				displayTypeName: "HWND",
				targetObjectType: "HWND"))
		{
		}
	}

	private sealed class NativeAutomationElementTargetObjectWrapper : TargetObjectWrapper
	{
		public NativeAutomationElementTargetObjectWrapper(AutomationElement target)
			: base(target, CreateMetadata(
				target,
				TargetObjectKind.NativeAutomationElement,
				"native-automation",
				canReceiveActions: true,
				TryGetNativeHwnd(target)))
		{
		}

		private static long? TryGetNativeHwnd(AutomationElement target)
		{
			try
			{
				var hwnd = target.Current.NativeWindowHandle;
				return hwnd == 0 ? null : hwnd;
			}
			catch (ElementNotAvailableException)
			{
				return null;
			}
			catch (InvalidOperationException)
			{
				return null;
			}
		}
	}

	private sealed class UnknownTargetObjectWrapper : TargetObjectWrapper
	{
		public UnknownTargetObjectWrapper(object target)
			: base(target, CreateMetadata(target, TargetObjectKind.Unknown, "unknown", canReceiveActions: false))
		{
		}
	}
}
