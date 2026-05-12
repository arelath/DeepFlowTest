namespace DeepFlowTest.Tests;

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using DeepFlowTest.Utility.WpfUtility.Tree;
using NUnit.Framework;
using FormsButton = System.Windows.Forms.Button;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class TargetObjectWrapperTests
{
	[Test]
	public void WpfWrapperCreatesStableMetadata()
	{
		var button = new Button { Name = "okButton", Content = "OK" };
		using var wrapper = TargetObjectWrapper.Create(button);

		Assert.That(wrapper.Metadata.Kind, Is.EqualTo(TargetObjectKind.WpfVisual));
		Assert.That(wrapper.Metadata.RuntimeFamily, Is.EqualTo("wpf"));
		Assert.That(wrapper.Metadata.DisplayTypeName, Is.EqualTo("Button"));
		Assert.That(wrapper.Metadata.TargetObjectType, Does.Contain("Button"));
		Assert.That(wrapper.Metadata.CanReceiveActions, Is.True);
		Assert.That(wrapper.Metadata.Hwnd, Is.Null);
		Assert.That(wrapper.TryGetTarget(out var target), Is.True);
		Assert.That(target, Is.SameAs(button));
	}

	[Test]
	public void WinFormsWrapperCreatesStableMetadata()
	{
		using var button = new FormsButton { Text = "OK" };
		using var wrapper = TargetObjectWrapper.Create(button);

		Assert.That(wrapper.Metadata.Kind, Is.EqualTo(TargetObjectKind.WinFormsControl));
		Assert.That(wrapper.Metadata.RuntimeFamily, Is.EqualTo("winforms"));
		Assert.That(wrapper.Metadata.DisplayTypeName, Is.EqualTo("Button"));
		Assert.That(wrapper.Metadata.CanReceiveActions, Is.True);
		Assert.That(wrapper.Metadata.Hwnd, Is.Null);
	}

	[Test]
	public void NativeWindowWrapperRecordsHwnd()
	{
		var wrapper = TargetObjectWrapper.Create(new IntPtr(1234));

		Assert.That(wrapper.Metadata.Kind, Is.EqualTo(TargetObjectKind.NativeWindow));
		Assert.That(wrapper.Metadata.RuntimeFamily, Is.EqualTo("native"));
		Assert.That(wrapper.Metadata.DisplayTypeName, Is.EqualTo("HWND"));
		Assert.That(wrapper.Metadata.Hwnd, Is.EqualTo(1234));
		Assert.That(wrapper.Metadata.CanReceiveActions, Is.True);
	}

	[Test]
	public void ResourceAndImageWrappersAreNonActionable()
	{
		using var resourceWrapper = TargetObjectWrapper.Create(new ResourceDictionary());
		using var imageWrapper = TargetObjectWrapper.Create(new System.Drawing.Bitmap(1, 1));

		Assert.That(resourceWrapper.Metadata.Kind, Is.EqualTo(TargetObjectKind.Resource));
		Assert.That(resourceWrapper.Metadata.CanReceiveActions, Is.False);
		Assert.That(imageWrapper.Metadata.Kind, Is.EqualTo(TargetObjectKind.Image));
		Assert.That(imageWrapper.Metadata.CanReceiveActions, Is.False);
	}

	[Test]
	public void DisposeReleasesStrongTargetReference()
	{
		var target = new Button();
		var wrapper = TargetObjectWrapper.Create(target);

		wrapper.Dispose();

		Assert.That(wrapper.IsDisposed, Is.True);
		Assert.That(wrapper.TryGetTarget(out var currentTarget), Is.False);
		Assert.That(currentTarget, Is.Null);
		Assert.That(() => _ = wrapper.Target, Throws.TypeOf<ObjectDisposedException>());
	}
}
