namespace DeepFlowTest.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation.Peers;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using NUnit.Framework;
using static DeepFlowTest.Tests.WpfTestHelpers;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class TreeServiceTests
{
	[Test]
	public void WpfRootDiscoveryBuildsSnapshotFromShownWindow()
	{
		var button = new Button { Name = "helloButton", Content = "Hello" };
		var window = CreateWindow("Root discovery", button);

		try
		{
			window.Show();

			var snapshot = new TreeService().CaptureSnapshot(new TreeSnapshotOptions
			{
				RequestedPropertyNames = new[] { KnownProperties.Name, KnownProperties.Content, KnownProperties.Title },
				MaxNodeCount = 200,
			});

			Assert.That(snapshot.RootIds, Is.Not.Empty);
			Assert.That(snapshot.TargetFrameworkFamily, Is.EqualTo("wpf"));
			Assert.That(snapshot.Nodes.Any(static node => node.TypeName == "Window"), Is.True);

			var buttonNode = snapshot.Nodes.SingleOrDefault(node =>
				node.Properties.TryGetValue(KnownProperties.Name, out var name) && Equals(name, "helloButton"));
			Assert.That(buttonNode, Is.Not.Null);
			Assert.That(buttonNode!.ParentId, Is.Not.Null);
			Assert.That(buttonNode.Properties[KnownProperties.Content], Is.EqualTo("Hello"));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void MultipleWindowsAndPopupChildAppearInSnapshot()
	{
		var firstPanel = new StackPanel();
		var popup = new Popup
		{
			Name = "testPopup",
			Child = new TextBlock { Name = "popupText", Text = "Popup" },
		};
		firstPanel.Children.Add(new Button { Name = "firstButton", Content = "First" });
		firstPanel.Children.Add(popup);

		var firstWindow = CreateWindow("First", firstPanel);
		var secondWindow = CreateWindow("Second", new TextBlock { Name = "secondText", Text = "Second" });

		try
		{
			firstWindow.Show();
			secondWindow.Show();
			popup.IsOpen = true;

			var snapshot = new TreeService().CaptureSnapshot(new TreeSnapshotOptions
			{
				RequestedPropertyNames = new[] { KnownProperties.Name, KnownProperties.Text, KnownProperties.Title },
				MaxNodeCount = 300,
			});

			Assert.That(snapshot.Nodes.Count(static node => node.TypeName == "Window"), Is.GreaterThanOrEqualTo(2));
			Assert.That(HasNodeNamed(snapshot, "secondText"), Is.True);
			Assert.That(HasNodeNamed(snapshot, "popupText"), Is.True);
		}
		finally
		{
			popup.IsOpen = false;
			secondWindow.Close();
			firstWindow.Close();
		}
	}

	[Test]
	public void DeepFlowTestHelperObjectsAreExcludedFromSnapshots()
	{
		var panel = new StackPanel();
		panel.Children.Add(new Button { Name = "realButton", Content = "Real" });
		panel.Children.Add(new DiagnosticHelperButton { Name = "helperButton", Content = "Helper" });
		var window = CreateWindow("Helper filter", panel);

		try
		{
			window.Show();

			var snapshot = new TreeService().CaptureSnapshot(new TreeSnapshotOptions
			{
				RequestedPropertyNames = new[] { KnownProperties.Name, KnownProperties.Content },
				MaxNodeCount = 200,
			});

			Assert.That(HasNodeNamed(snapshot, "realButton"), Is.True);
			Assert.That(HasNodeNamed(snapshot, "helperButton"), Is.False);
			Assert.That(snapshot.Nodes.Any(static node => node.FrameworkTypeName?.Contains(nameof(DiagnosticHelperButton)) == true), Is.False);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void WrapperModelExposesTargetKindResourcesImagesWebBrowserAndAutomationPeers()
	{
		var image = new Image
		{
			Name = "treeImage",
			Width = 2,
			Height = 2,
			Source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 0 }, 4),
		};
		var panel = new StackPanel { Name = "breadthPanel" };
		panel.Resources["resourceImage"] = image.Source;
		panel.Children.Add(image);
		panel.Children.Add(new WebBrowser { Name = "treeWebBrowser", Width = 10, Height = 10 });
		var window = CreateWindow("Breadth", panel);

		try
		{
			window.Show();

			var snapshot = new TreeService(rootProvider: () => [window, new SystemResourceRoot()]).CaptureSnapshot(new TreeSnapshotOptions
			{
				RequestedPropertyNames = new[] { KnownProperties.Name, "Width", "Height", "Count", KnownProperties.ResourceKeys, KnownProperties.ImageMetadata, KnownProperties.Xaml, KnownProperties.ResourceOrigin },
				MaxNodeCount = 500,
			});

			var webBrowserNode = snapshot.Nodes.Single(node => node.TypeName == "WebBrowser");
			Assert.That(webBrowserNode.TargetKind, Is.EqualTo(TargetObjectKind.WebBrowser.ToString()));
			Assert.That(webBrowserNode.RuntimeFamily, Is.EqualTo("browser"));
			Assert.That(snapshot.Nodes.Any(node => node.TargetKind == TargetObjectKind.Resource.ToString()), Is.True);
			Assert.That(snapshot.Nodes.Any(node => node.TargetKind == TargetObjectKind.SystemResource.ToString()), Is.True);
			Assert.That(snapshot.Nodes.Any(node => node.TargetKind == TargetObjectKind.Image.ToString()), Is.True);
			Assert.That(snapshot.Nodes.Any(node => node.RuntimeFamily == "image"), Is.True);
			var imageNode = snapshot.Nodes.Single(node => node.Properties.TryGetValue(KnownProperties.Name, out var name) && Equals(name, "treeImage"));
			Assert.That(imageNode.CanReceiveActions, Is.True);
			Assert.That(imageNode.Properties[KnownProperties.ImageMetadata], Is.Not.Null);
			Assert.That(snapshot.Nodes.Any(node =>
				node.TargetKind == TargetObjectKind.Resource.ToString()
				&& node.Properties.TryGetValue(KnownProperties.ResourceKeys, out var keys)
				&& keys is System.Collections.IEnumerable resourceKeys
				&& resourceKeys.Cast<object?>().Any(key => Equals(key, "resourceImage"))), Is.True);

			var targetIds = new TargetIdService();
			var peerId = targetIds.GetOrCreateId(UIElementAutomationPeer.CreatePeerForElement(image));
			var peerSnapshot = new TreeService(targetIds).CaptureSnapshot(new TreeSnapshotOptions
			{
				RootTargetId = peerId,
				RequestedPropertyNames = new[] { KnownProperties.ClassName },
				MaxNodeCount = 10,
			});

			Assert.That(peerSnapshot.Nodes.Single().TargetKind, Is.EqualTo(TargetObjectKind.WpfAutomationPeer.ToString()));
			Assert.That(peerSnapshot.Nodes.Single().CanReceiveActions, Is.False);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void DefaultSnapshotAndRecordedTargetUseSourceForUnnamedImageIdentity()
	{
		var unnamedImage = new Image
		{
			Source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 0 }, 4),
		};
		var namedImage = new Image
		{
			Name = "namedImage",
			Source = unnamedImage.Source,
		};
		var treeService = new TreeService(rootProvider: () => [unnamedImage, namedImage]);

		var snapshot = treeService.CaptureSnapshot();
		var unnamedNode = snapshot.Nodes.Single(node => node.TypeName == "Image" && !Equals(node.Properties[KnownProperties.Name], "namedImage"));
		var namedNode = snapshot.Nodes.Single(node => Equals(node.Properties[KnownProperties.Name], "namedImage"));
		var recordedTarget = treeService.DescribeTargetForRecording(unnamedImage);

		Assert.Multiple(() =>
		{
			Assert.That(unnamedNode.Properties[KnownProperties.Source], Is.Not.Null.And.Not.Empty);
			Assert.That(namedNode.Properties.ContainsKey(KnownProperties.Source), Is.False);
			Assert.That(recordedTarget.Properties[KnownProperties.Source], Is.EqualTo(unnamedNode.Properties[KnownProperties.Source]));
			Assert.That(recordedTarget.SelectorHints.Any(hint => hint.PropertyName == KnownProperties.Source && hint.Kind == "source"), Is.True);
		});
	}

	[Test]
	public void ResourceDictionariesExposeMergedDictionariesAndSystemResources()
	{
		var merged = new ResourceDictionary
		{
			["mergedBrush"] = Brushes.CadetBlue,
		};
		var panel = new StackPanel { Name = "resourcePanel" };
		panel.Resources.MergedDictionaries.Add(merged);
		panel.Resources["localBrush"] = Brushes.Coral;
		var window = CreateWindow("Resource dictionaries", panel);

		try
		{
			window.Show();

			var snapshot = new TreeService(rootProvider: () => [window, new SystemResourceRoot()]).CaptureSnapshot(new TreeSnapshotOptions
			{
				RequestedPropertyNames = new[] { KnownProperties.Name, KnownProperties.ResourceKeys, KnownProperties.MergedDictionaryCount, KnownProperties.ResourceOrigin },
				MaxNodeCount = 500,
			});

			var dictionaries = snapshot.Nodes.Where(node => node.TargetKind == TargetObjectKind.Resource.ToString()).ToArray();
			Assert.That(dictionaries.Any(node => Equals(node.Properties[KnownProperties.MergedDictionaryCount], 1)), Is.True);
			Assert.That(dictionaries.Any(node =>
				node.Properties.TryGetValue(KnownProperties.ResourceKeys, out var keys)
				&& keys is System.Collections.IEnumerable resourceKeys
				&& resourceKeys.Cast<object?>().Any(key => Equals(key, "mergedBrush"))), Is.True);
			Assert.That(snapshot.Nodes.Any(node => node.TargetKind == TargetObjectKind.SystemResource.ToString()), Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ExplicitHelperPropertiesExposeBindingsAndXaml()
	{
		var source = new BindingSource { Caption = "Bound value" };
		var textBlock = new TextBlock { Name = "boundText" };
		textBlock.SetBinding(TextBlock.TextProperty, new Binding(nameof(BindingSource.Caption)) { Source = source });
		var window = CreateWindow("Binding helpers", textBlock);

		try
		{
			window.Show();

			var snapshot = new TreeService().CaptureSnapshot(new TreeSnapshotOptions
			{
				RequestedPropertyNames = new[] { KnownProperties.Name, KnownProperties.Text, KnownProperties.Bindings, KnownProperties.Xaml },
				MaxNodeCount = 200,
			});

			var textNode = snapshot.Nodes.Single(node => node.Properties.TryGetValue(KnownProperties.Name, out var name) && Equals(name, "boundText"));
			Assert.That(textNode.Properties[KnownProperties.Text], Is.EqualTo("Bound value"));
			Assert.That(textNode.Properties[KnownProperties.Xaml]?.ToString(), Does.Contain("TextBlock"));
			var bindings = (IReadOnlyDictionary<string, object?>)textNode.Properties[KnownProperties.Bindings]!;
			Assert.That(bindings.ContainsKey(KnownProperties.Text), Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	private static bool HasNodeNamed(DeepFlowTest.Interop.VisualTreeSnapshot snapshot, string name)
	{
		return snapshot.Nodes.Any(node =>
			node.Properties.TryGetValue(KnownProperties.Name, out var value) && Equals(value, name));
	}

	private sealed class BindingSource
	{
		public string Caption { get; set; } = string.Empty;
	}
}
