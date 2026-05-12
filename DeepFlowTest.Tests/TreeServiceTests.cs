namespace DeepFlowTest.Tests;

using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.Utility.WpfUtility.Tree;
using NUnit.Framework;

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
				RequestedPropertyNames = new[] { "Name", "Content", "Title" },
				MaxNodeCount = 200,
			});

			Assert.That(snapshot.RootIds, Is.Not.Empty);
			Assert.That(snapshot.TargetFrameworkFamily, Is.EqualTo("wpf"));
			Assert.That(snapshot.Nodes.Any(static node => node.TypeName == "Window"), Is.True);

			var buttonNode = snapshot.Nodes.SingleOrDefault(node =>
				node.Properties.TryGetValue("Name", out var name) && Equals(name, "helloButton"));
			Assert.That(buttonNode, Is.Not.Null);
			Assert.That(buttonNode!.ParentId, Is.Not.Null);
			Assert.That(buttonNode.Properties["Content"], Is.EqualTo("Hello"));
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
				RequestedPropertyNames = new[] { "Name", "Text", "Title" },
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
				RequestedPropertyNames = new[] { "Name", "Content" },
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

	private static Window CreateWindow(string title, object content)
	{
		return new Window
		{
			Title = title,
			Content = content,
			Width = 240,
			Height = 160,
			ShowInTaskbar = false,
			WindowStartupLocation = WindowStartupLocation.Manual,
			Left = -20000,
			Top = -20000,
		};
	}

	private static bool HasNodeNamed(DeepFlowTest.Interop.VisualTreeSnapshot snapshot, string name)
	{
		return snapshot.Nodes.Any(node =>
			node.Properties.TryGetValue("Name", out var value) && Equals(value, name));
	}
}
