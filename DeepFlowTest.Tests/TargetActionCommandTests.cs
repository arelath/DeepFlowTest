namespace DeepFlowTest.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;
using DeepFlowTest.AppDriverPayload.Diagnostics;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using NUnit.Framework;
using static DeepFlowTest.Tests.TestIpcHost;
using static DeepFlowTest.Tests.WpfTestHelpers;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class TargetActionCommandTests
{
	[Test]
	public void ClickAndKnownRoutedEventChangeHarnessState()
	{
		var clickCount = 0;
		var rightClickCount = 0;
		var button = new Button { Name = "actionButton", Content = "Ready" };
		button.Click += (_, _) =>
		{
			clickCount++;
			button.Content = "Clicked";
		};
		button.MouseRightButtonUp += (_, _) => rightClickCount++;
		var window = CreateWindow("Click actions", button);

		try
		{
			window.Show();
			var targetId = FindTargetId("actionButton");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId }));
			Assert.That(clickCount, Is.EqualTo(1));
			Assert.That(button.Content, Is.EqualTo("Clicked"));

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId, ClickCount = 2 }));
			Assert.That(clickCount, Is.EqualTo(3));

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId, MouseButton = MouseButtonKind.Right }));
			Assert.That(rightClickCount, Is.EqualTo(1));

			AssertOk(CaptureResponse(new KnownRoutedEventCommandRequest { TargetId = targetId, EventName = "Click" }));
			Assert.That(clickCount, Is.EqualTo(4));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void SyntheticClickReportsVirtualPointerTelemetryWhenEnabled()
	{
		var renderer = new RecordingVirtualPointerRenderer();
		using var _ = VirtualPointerService.UseRendererFactoryForTests(_ => renderer);
		VirtualPointerService.Configure(new VirtualPointerOptionsDto { Enabled = true, HideDelayMs = 0 });
		var button = new Button { Name = "virtualPointerClickButton", Content = "Pointer" };
		var window = CreateWindow("Virtual pointer click", button);

		try
		{
			window.Show();
			window.UpdateLayout();
			DoEvents();
			var targetId = FindTargetId("virtualPointerClickButton");
			var expectedPoint = button.PointToScreen(new Point(button.RenderSize.Width / 2, button.RenderSize.Height / 2));

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId }));

			Assert.That(renderer.MovePoints, Has.Count.EqualTo(1));
			Assert.That(renderer.MovePoints[0].X, Is.EqualTo(expectedPoint.X).Within(1));
			Assert.That(renderer.MovePoints[0].Y, Is.EqualTo(expectedPoint.Y).Within(1));
			Assert.That(renderer.Clicks, Is.EqualTo(new[] { 1 }));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void KnownMouseDoubleClickReportsVirtualPointerTelemetryWhenEnabled()
	{
		var renderer = new RecordingVirtualPointerRenderer();
		using var _ = VirtualPointerService.UseRendererFactoryForTests(_ => renderer);
		VirtualPointerService.Configure(new VirtualPointerOptionsDto { Enabled = true, HideDelayMs = 0 });
		var button = new Button { Name = "virtualPointerDoubleClickButton", Content = "Pointer" };
		var window = CreateWindow("Virtual pointer double click", button);

		try
		{
			window.Show();
			window.UpdateLayout();
			DoEvents();
			var targetId = FindTargetId("virtualPointerDoubleClickButton");

			AssertOk(CaptureResponse(new KnownRoutedEventCommandRequest { TargetId = targetId, EventName = "MouseDoubleClick" }));

			Assert.That(renderer.MovePoints, Has.Count.EqualTo(1));
			Assert.That(renderer.Clicks, Is.EqualTo(new[] { 2 }));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void KnownRoutedEventUsesTargetSpecificClickEventForMenuItem()
	{
		var clickCount = 0;
		var menu = new Menu();
		var menuItem = new MenuItem { Name = "menuItem", Header = "MenuItem" };
		menuItem.Click += (_, _) => clickCount++;
		menu.Items.Add(menuItem);
		var window = CreateWindow("Menu item routed event", menu);

		try
		{
			window.Show();
			var targetId = FindTargetId("menuItem");

			AssertOk(CaptureResponse(new KnownRoutedEventCommandRequest { TargetId = targetId, EventName = "Click" }));

			Assert.That(clickCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void MenuHeaderReopensAfterNestedSubmenuIsClosedWithEscapeTwice()
	{
		var menu = new Menu();
		var header = new MenuItem { Name = "reopenMenuHeader", Header = "Menu Header" };
		var nestedHeader = new MenuItem { Name = "reopenNestedMenuHeader", Header = "Nested" };
		nestedHeader.Items.Add(new MenuItem { Name = "reopenNestedLeaf", Header = "Leaf" });
		header.Items.Add(nestedHeader);
		menu.Items.Add(header);
		var window = CreateWindow("Menu reopen after escape", menu);

		try
		{
			window.Left = 0;
			window.Top = 0;
			window.Show();
			window.Activate();
			DoEvents();
			var headerId = FindTargetId("reopenMenuHeader");
			var nestedHeaderId = FindTargetId("reopenNestedMenuHeader");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = headerId }));
			Assert.That(WaitUntil(() => header.IsSubmenuOpen), Is.True, "Initial menu header click should open the menu.");

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = headerId, Operation = "Collapse" }));
			Assert.That(WaitUntil(() => !header.IsSubmenuOpen), Is.True, "Collapse should close the menu header.");

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = headerId, Operation = "Expand" }));
			Assert.That(WaitUntil(() => header.IsSubmenuOpen), Is.True, "Expand should open the menu header.");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = nestedHeaderId }));
			Assert.That(WaitUntil(() => nestedHeader.IsSubmenuOpen), Is.True, "Nested submenu click should open the nested menu.");

			AssertOk(CaptureResponse(new KeyPressCommandRequest
			{
				TargetId = nestedHeaderId,
				Keys = "Escape",
				DelayMs = 1,
				EnsureForeground = true,
			}));
			Assert.That(WaitUntil(() => !nestedHeader.IsSubmenuOpen), Is.True, "First Escape should close the nested submenu.");

			AssertOk(CaptureResponse(new KeyPressCommandRequest
			{
				TargetId = headerId,
				Keys = "Escape",
				DelayMs = 1,
				EnsureForeground = true,
			}));
			Assert.That(WaitUntil(() => !header.IsSubmenuOpen), Is.True, "Second Escape should close the parent menu.");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = headerId }));

			Assert.That(WaitUntil(() => header.IsSubmenuOpen), Is.True, "Menu header should reopen after closing with Escape twice.");
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ClickRaisesWpfMouseEventsDoubleClickAndContextMenu()
	{
		var previewDownCount = 0;
		var downCount = 0;
		var upCount = 0;
		var doubleClickCount = 0;
		Point? lastMouseDownPosition = null;
		var buttonEvents = new List<string>();
		var panel = new StackPanel();
		var border = new Border
		{
			Name = "mouseBorder",
			Width = 60,
			Height = 40,
			ContextMenu = new ContextMenu(),
		};
		var button = new Button { Name = "doubleClickButton", Content = "Double" };
		border.PreviewMouseDown += (_, _) => previewDownCount++;
		border.MouseDown += (_, args) =>
		{
			downCount++;
			lastMouseDownPosition = args.GetPosition(border);
		};
		border.MouseUp += (_, _) => upCount++;
		button.Click += (_, _) => buttonEvents.Add("Click");
		button.MouseDoubleClick += (_, _) =>
		{
			doubleClickCount++;
			buttonEvents.Add("MouseDoubleClick");
		};
		panel.Children.Add(border);
		panel.Children.Add(button);
		var window = CreateWindow("Mouse routed actions", panel);

		try
		{
			window.Show();
			var borderId = FindTargetId("mouseBorder");
			var buttonId = FindTargetId("doubleClickButton");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = borderId }));
			Assert.That(previewDownCount, Is.EqualTo(1));
			Assert.That(downCount, Is.EqualTo(1));
			Assert.That(upCount, Is.EqualTo(1));
			Assert.That(lastMouseDownPosition?.X, Is.EqualTo(30).Within(0.5));
			Assert.That(lastMouseDownPosition?.Y, Is.EqualTo(20).Within(0.5));

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = buttonId, ClickCount = 2 }));
			Assert.That(doubleClickCount, Is.EqualTo(1));
			Assert.That(buttonEvents.Last(), Is.EqualTo("MouseDoubleClick"));

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = borderId, MouseButton = MouseButtonKind.Right }));
			Assert.That(border.ContextMenu.IsOpen, Is.True);
			border.ContextMenu.IsOpen = false;
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ClickSelectsWpfTabItem()
	{
		var tabControl = new TabControl();
		var firstTab = new TabItem { Name = "firstSyntheticClickTab", Header = "First", Content = "First Content" };
		var secondTab = new TabItem { Name = "secondSyntheticClickTab", Header = "Second", Content = "Second Content" };
		tabControl.Items.Add(firstTab);
		tabControl.Items.Add(secondTab);
		var window = CreateWindow("Synthetic click tab selection", tabControl);

		try
		{
			window.Show();
			var secondTabId = FindTargetId("secondSyntheticClickTab");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = secondTabId }));

			Assert.That(WaitUntil(() => secondTab.IsSelected), Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ClickTogglesWpfToggleButton()
	{
		var toggle = new ToggleButton { Name = "syntheticClickToggle", IsChecked = false, Content = "Toggle" };
		var window = CreateWindow("Synthetic toggle click", toggle);

		try
		{
			window.Show();
			var targetId = FindTargetId("syntheticClickToggle");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId }));
			Assert.That(toggle.IsChecked, Is.True);

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId }));
			Assert.That(toggle.IsChecked, Is.False);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ClickFocusesWpfTextBoxThroughInputManagerRouting()
	{
		var panel = new StackPanel();
		var first = new TextBox { Name = "inputManagerClickFocusFirst", Width = 120 };
		var second = new TextBox { Name = "inputManagerClickFocusSecond", Width = 120 };
		panel.Children.Add(first);
		panel.Children.Add(second);
		var window = CreateWindow("InputManager click focus", panel);

		try
		{
			window.Show();
			window.UpdateLayout();
			second.Focus();
			DoEvents();
			Assert.That(second.IsKeyboardFocusWithin, Is.True);
			var firstId = FindTargetId("inputManagerClickFocusFirst");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = firstId }));

			Assert.That(WaitUntil(() => first.IsKeyboardFocusWithin), Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void VirtualPointerRendererAttachesAdornerInsideOwnerWindow()
	{
		var root = new Grid
		{
			Name = "virtualPointerAdornerRoot",
			Width = 180,
			Height = 90,
			Background = Brushes.Transparent,
		};
		var window = CreateWindow("Virtual pointer adorner", root, width: 240, height: 160);

		try
		{
			window.Show();
			window.UpdateLayout();
			DoEvents();
			var windowCount = Application.Current?.Windows.Count ?? 0;
			using var renderer = new WpfAdornerVirtualPointerRenderer(
				Dispatcher.CurrentDispatcher,
				new VirtualPointerOptionsDto { Enabled = true, HideDelayMs = 0 });
			var handle = new WindowInteropHelper(window).Handle;

			renderer.MoveTo(root.PointToScreen(new Point(20, 20)), handle);
			renderer.Click(MouseButtonKind.Left, 1);
			DoEvents();

			var layer = AdornerLayer.GetAdornerLayer(root);
			Assert.That(layer, Is.Not.Null);
			var adorners = layer!.GetAdorners(root);
			Assert.That(adorners?.OfType<VirtualPointerAdorner>().Count(), Is.EqualTo(1));
			Assert.That(Application.Current?.Windows.Count ?? 0, Is.EqualTo(windowCount));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void RightClickOnWpfButtonOpensContextMenuThatCanBeFoundAndClicked()
	{
		var rightButtonDownCount = 0;
		var contextMenuClickCount = 0;
		var status = new TextBlock { Name = "documentTabStatus", Text = "Ready" };
		var closeMenuItem = new MenuItem
		{
			Name = "closeDocumentContextMenuItem",
			Header = "Close Document",
		};
		closeMenuItem.Click += (_, _) =>
		{
			contextMenuClickCount++;
			status.Text = "Closed";
		};
		var contextMenu = new ContextMenu();
		contextMenu.Items.Add(closeMenuItem);
		var documentTab = new Button
		{
			Name = "midiDocumentTabButton",
			Content = "Midi Document",
			ContextMenu = contextMenu,
			Width = 140,
			Height = 32,
		};
		documentTab.MouseRightButtonDown += (_, args) =>
		{
			rightButtonDownCount++;
			args.Handled = true;
		};
		var panel = new StackPanel();
		panel.Children.Add(documentTab);
		panel.Children.Add(status);
		var window = CreateWindow("Document tab context menu", panel);

		try
		{
			window.Show();
			var documentTabId = FindTargetId("midiDocumentTabButton");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = documentTabId, MouseButton = MouseButtonKind.Right }));

			Assert.That(rightButtonDownCount, Is.EqualTo(1));
			Assert.That(WaitUntil(() => contextMenu.IsOpen), Is.True, "Right-clicking a WPF Button should open its ContextMenu.");
			Assert.That(contextMenu.PlacementTarget, Is.SameAs(documentTab));

			var menuItemId = FindTargetId("closeDocumentContextMenuItem");
			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = menuItemId }));

			Assert.That(contextMenuClickCount, Is.EqualTo(1));
			Assert.That(status.Text, Is.EqualTo("Closed"));
		}
		finally
		{
			contextMenu.IsOpen = false;
			window.Close();
		}
	}

	// Repro for container styles that declare the ContextMenu on the item container
	// (Actipro's TabItemContainerStyle on DockingWindowContainerTabItem) while the only
	// element a test can address is a child of the header - typically the TextBlock that
	// carries the tab title. WPF walks up from the element under the pointer to the nearest
	// ancestor that owns a ContextMenu and makes that ancestor the PlacementTarget, so the
	// right-click path has to do the same or the menu never opens (and PlacementTarget.*
	// bindings inside it resolve against the wrong element).
	[Test]
	public void RightClickOpensContextMenuDeclaredOnAncestorContainer()
	{
		var contextMenu = new ContextMenu();
		contextMenu.Items.Add(new MenuItem { Header = "Close other tabs", Name = "ancestorMenuCloseOtherTabs" });
		var tabTitle = new TextBlock { Name = "ancestorMenuTabTitle", Text = "sinkPedC_01" };
		var tabContainer = new Border
		{
			Name = "ancestorMenuTabContainer",
			Background = Brushes.Transparent,
			Width = 140,
			Height = 32,
			ContextMenu = contextMenu,
			Child = tabTitle,
		};
		var window = CreateWindow("Ancestor-owned context menu", tabContainer);

		try
		{
			window.Show();
			var tabTitleId = FindTargetId("ancestorMenuTabTitle");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = tabTitleId, MouseButton = MouseButtonKind.Right }));

			Assert.That(
				WaitUntil(() => contextMenu.IsOpen),
				Is.True,
				"Right-clicking a child of the container should open the container's ContextMenu.");
			Assert.That(
				contextMenu.PlacementTarget,
				Is.SameAs(tabContainer),
				"PlacementTarget must be the element that declares the ContextMenu, not the clicked child.");

			// The opened menu's items must be addressable so tests can click them.
			Assert.That(FindTargetId("ancestorMenuCloseOtherTabs"), Is.Not.Empty);
		}
		finally
		{
			contextMenu.IsOpen = false;
			window.Close();
		}
	}

	// Repro for Actipro's docking-tab context menu, which does not use a static
	// ContextMenu property — instead it subscribes to ContextMenuOpening and
	// assembles the menu on the fly in response. ClickWpfElement's right-click
	// path must raise ContextMenuOpeningEvent for that pattern to fire.
	[Test]
	public void RightClickRaisesContextMenuOpeningForDynamicallyBuiltMenu()
	{
		var contextMenuOpeningCount = 0;
		var dynamicallyBuiltMenuOpened = false;
		ContextMenu? builtMenu = null;
		var documentTab = new Button
		{
			Name = "dynamicMenuDocumentTab",
			Content = "Tab",
			Width = 140,
			Height = 32,
		};
		documentTab.ContextMenuOpening += (sender, args) =>
		{
			contextMenuOpeningCount++;
			builtMenu = new ContextMenu
			{
				PlacementTarget = documentTab,
				Placement = PlacementMode.Bottom,
			};
			builtMenu.Items.Add(new MenuItem { Header = "Close Others", Name = "dynamicMenuCloseOthers" });
			builtMenu.Opened += (_, _) => dynamicallyBuiltMenuOpened = true;
			builtMenu.IsOpen = true;
			args.Handled = true;
		};
		var window = CreateWindow("Dynamic context menu", documentTab);

		try
		{
			window.Show();
			var documentTabId = FindTargetId("dynamicMenuDocumentTab");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = documentTabId, MouseButton = MouseButtonKind.Right }));

			Assert.That(contextMenuOpeningCount, Is.EqualTo(1), "Right-click should raise ContextMenuOpening on the target.");
			Assert.That(WaitUntil(() => dynamicallyBuiltMenuOpened), Is.True, "Dynamically-built ContextMenu should open.");
		}
		finally
		{
			if (builtMenu is not null)
				builtMenu.IsOpen = false;
			window.Close();
		}
	}

	[Test]
	public void ClickCountTwoTriggersWpfMouseBindingDoubleClickGesture()
	{
		var commandCount = 0;
		var command = new TestCommand(() => commandCount++);
		var target = new Border
		{
			Name = "doubleClickGestureTarget",
			Width = 80,
			Height = 40,
			Background = Brushes.Transparent,
			Focusable = true,
		};
		target.InputBindings.Add(new MouseBinding(command, new MouseGesture(MouseAction.LeftDoubleClick)));
		var window = CreateWindow("MouseBinding double click", target);

		try
		{
			window.Show();
			var targetId = FindTargetId("doubleClickGestureTarget");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId, ClickCount = 2 }));

			Assert.That(commandCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void RaiseMouseDoubleClickTriggersWpfMouseBindingDoubleClickGesture()
	{
		var commandCount = 0;
		var doubleClickCount = 0;
		var command = new TestCommand(() => commandCount++);
		var target = new Button
		{
			Name = "raiseDoubleClickGestureTarget",
			Content = "Double",
			Width = 80,
			Height = 32,
		};
		target.MouseDoubleClick += (_, _) => doubleClickCount++;
		target.InputBindings.Add(new MouseBinding(command, new MouseGesture(MouseAction.LeftDoubleClick)));
		var window = CreateWindow("Raised MouseBinding double click", target);

		try
		{
			window.Show();
			var targetId = FindTargetId("raiseDoubleClickGestureTarget");

			AssertOk(CaptureResponse(new RaiseEventCommandRequest { TargetId = targetId, EventName = "MouseDoubleClick" }));

			Assert.That(commandCount, Is.EqualTo(1));
			Assert.That(doubleClickCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ClickTriggersWpfMouseBindingClickGestures()
	{
		var leftClickCount = 0;
		var rightClickCount = 0;
		var leftCommand = new TestCommand(() => leftClickCount++);
		var rightCommand = new TestCommand(() => rightClickCount++);
		var panel = new StackPanel();
		var leftTarget = new Border
		{
			Name = "leftClickGestureTarget",
			Width = 80,
			Height = 40,
			Background = Brushes.Transparent,
		};
		var rightTarget = new Border
		{
			Name = "rightClickGestureTarget",
			Width = 80,
			Height = 40,
			Background = Brushes.Transparent,
		};
		leftTarget.InputBindings.Add(new MouseBinding(leftCommand, new MouseGesture(MouseAction.LeftClick)));
		rightTarget.InputBindings.Add(new MouseBinding(rightCommand, new MouseGesture(MouseAction.RightClick)));
		panel.Children.Add(leftTarget);
		panel.Children.Add(rightTarget);
		var window = CreateWindow("MouseBinding click gestures", panel);

		try
		{
			window.Show();
			var leftTargetId = FindTargetId("leftClickGestureTarget");
			var rightTargetId = FindTargetId("rightClickGestureTarget");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = leftTargetId }));
			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = rightTargetId, MouseButton = MouseButtonKind.Right }));

			Assert.That(leftClickCount, Is.EqualTo(1));
			Assert.That(rightClickCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void RightDoubleClickTriggersWpfMouseBindingDoubleClickGesture()
	{
		var commandCount = 0;
		var command = new TestCommand(() => commandCount++);
		var target = new Border
		{
			Name = "rightDoubleClickGestureTarget",
			Width = 80,
			Height = 40,
			Background = Brushes.Transparent,
		};
		target.InputBindings.Add(new MouseBinding(command, new MouseGesture(MouseAction.RightDoubleClick)));
		var window = CreateWindow("MouseBinding right double click", target);

		try
		{
			window.Show();
			var targetId = FindTargetId("rightDoubleClickGestureTarget");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId, MouseButton = MouseButtonKind.Right, ClickCount = 2 }));

			Assert.That(commandCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void KeyPressTriggersWpfKeyBindingGestureOnTarget()
	{
		var commandCount = 0;
		var command = new TestCommand(() => commandCount++);
		var target = new Button
		{
			Name = "keyBindingGestureTarget",
			Content = "Keys",
			Width = 80,
			Height = 32,
		};
		target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
		var window = CreateWindow("KeyBinding target gesture", target);

		try
		{
			window.Show();
			var targetId = FindTargetId("keyBindingGestureTarget");

			AssertOk(CaptureResponse(new KeyPressCommandRequest
			{
				TargetId = targetId,
				Keys = "Control+K",
				DelayMs = 1,
				EnsureForeground = false,
			}));

			Assert.That(commandCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void KeyPressTriggersRoutedCommandInputGestureOnAncestor()
	{
		var commandCount = 0;
		var routedCommand = new RoutedCommand();
		routedCommand.InputGestures.Add(new KeyGesture(Key.L, ModifierKeys.Control));
		var target = new Button
		{
			Name = "routedGestureTarget",
			Content = "Routed",
			Width = 80,
			Height = 32,
		};
		var window = CreateWindow("RoutedCommand gesture", target);
		window.CommandBindings.Add(new CommandBinding(routedCommand, (_, _) => commandCount++));

		try
		{
			window.Show();
			var targetId = FindTargetId("routedGestureTarget");

			AssertOk(CaptureResponse(new KeyPressCommandRequest
			{
				TargetId = targetId,
				Keys = "Control+L",
				DelayMs = 1,
				EnsureForeground = false,
			}));

			Assert.That(commandCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void TextBoxKeyPressTriggersKeyBindingWithoutInsertingShortcutText()
	{
		var commandCount = 0;
		var command = new TestCommand(() => commandCount++);
		var textBox = new TextBox
		{
			Name = "textBoxKeyBindingTarget",
			Text = "ready",
			Width = 120,
		};
		textBox.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
		var window = CreateWindow("TextBox KeyBinding gesture", textBox);

		try
		{
			window.Show();
			var targetId = FindTargetId("textBoxKeyBindingTarget");

			AssertOk(CaptureResponse(new KeyPressCommandRequest
			{
				TargetId = targetId,
				Keys = "Control+K",
				DelayMs = 1,
				EnsureForeground = false,
			}));

			Assert.That(commandCount, Is.EqualTo(1));
			Assert.That(textBox.Text, Is.EqualTo("ready"));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void KeyPressSpoofsWpfModifierStateDuringInjectedKeyEvents()
	{
		var previewCount = 0;
		var keyDownCount = 0;
		var observedModifiers = ModifierKeys.None;
		var observedCtrlDown = false;
		var observedKeyDown = false;
		var target = new Button
		{
			Name = "modifierStateKeyTarget",
			Content = "Keys",
			Width = 80,
			Height = 32,
		};
		target.PreviewKeyDown += (_, args) =>
		{
			if (args.Key == Key.K)
				previewCount++;
		};
		target.KeyDown += (_, args) =>
		{
			if (args.Key != Key.K)
				return;

			keyDownCount++;
			observedModifiers = Keyboard.Modifiers;
			observedCtrlDown = Keyboard.IsKeyDown(Key.LeftCtrl);
			observedKeyDown = Keyboard.PrimaryDevice.GetKeyStates(Key.K).HasFlag(KeyStates.Down);
		};
		var window = CreateWindow("WPF synthetic keyboard state", target);

		try
		{
			window.Show();
			var targetId = FindTargetId("modifierStateKeyTarget");

			AssertOk(CaptureResponse(new KeyPressCommandRequest
			{
				TargetId = targetId,
				Keys = "Control+K",
				DelayMs = 1,
				EnsureForeground = false,
			}));

			Assert.That(previewCount, Is.EqualTo(1));
			Assert.That(keyDownCount, Is.EqualTo(1));
			Assert.That(observedModifiers, Is.EqualTo(ModifierKeys.Control));
			Assert.That(observedCtrlDown, Is.True);
			Assert.That(observedKeyDown, Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void FocusTypeTextAndKeyPressUpdateTextField()
	{
		var textBox = new TextBox { Name = "inputBox", Width = 120 };
		var window = CreateWindow("Text actions", textBox);

		try
		{
			window.Show();
			var targetId = FindTargetId("inputBox");

			AssertOk(CaptureResponse(new FocusCommandRequest { TargetId = targetId }));
			Assert.That(textBox.IsKeyboardFocusWithin, Is.True);

			AssertOk(CaptureResponse(new TypeTextCommandRequest { TargetId = targetId, Text = "abc", ClearFirst = true }));
			Assert.That(textBox.Text, Is.EqualTo("abc"));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = targetId, Keys = "d" }));
			Assert.That(textBox.Text, Is.EqualTo("abcd"));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = targetId, Keys = "Control+A", DelayMs = 1, EnsureForeground = true }));
			Assert.That(textBox.SelectionLength, Is.EqualTo(textBox.Text.Length));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void KeyPressTabMovesFocusToNextWpfElement()
	{
		var checkBox = new CheckBox { Name = "tabStart", Content = "Start", IsTabStop = true };
		var textBox = new TextBox { Name = "tabTarget", Width = 120, IsTabStop = true };
		var panel = new StackPanel();
		panel.Children.Add(checkBox);
		panel.Children.Add(textBox);
		var window = CreateWindow("Tab navigation", panel);

		try
		{
			window.Show();
			var startId = FindTargetId("tabStart");

			AssertOk(CaptureResponse(new KeyPressCommandRequest
			{
				TargetId = startId,
				Keys = "Tab",
				DelayMs = 1,
				EnsureForeground = false,
			}));

			Assert.That(textBox.IsKeyboardFocusWithin, Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void WpfKeyPressDeletesSelectedTextAndNavigatesBackward()
	{
		var first = new TextBox { Name = "firstEditBox", Text = "abcdef", Width = 120, IsTabStop = true };
		var second = new TextBox { Name = "secondEditBox", Width = 120, IsTabStop = true };
		var panel = new StackPanel();
		panel.Children.Add(first);
		panel.Children.Add(second);
		var window = CreateWindow("WPF keyboard editing", panel);

		try
		{
			window.Show();
			var firstId = FindTargetId("firstEditBox");
			var secondId = FindTargetId("secondEditBox");

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = firstId, Keys = "Control+A", DelayMs = 1, EnsureForeground = false }));
			Assert.That(first.SelectionLength, Is.EqualTo(6));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = firstId, Keys = "Backspace", DelayMs = 1, EnsureForeground = false }));
			Assert.That(first.Text, Is.Empty);
			Assert.That(first.CaretIndex, Is.EqualTo(0));

			first.Text = "abc";
			first.CaretIndex = 1;
			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = firstId, Keys = "Delete", DelayMs = 1, EnsureForeground = false }));
			Assert.That(first.Text, Is.EqualTo("ac"));
			Assert.That(first.CaretIndex, Is.EqualTo(1));

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = secondId, Keys = "Shift+Tab", DelayMs = 1, EnsureForeground = false }));
			Assert.That(first.IsKeyboardFocusWithin, Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void DisabledWpfButtonClickDoesNotRaiseClick()
	{
		var clickCount = 0;
		var button = new Button { Name = "disabledButton", Content = "Disabled", IsEnabled = false };
		button.Click += (_, _) => clickCount++;
		var window = CreateWindow("Disabled WPF click", button);

		try
		{
			window.Show();
			var targetId = FindTargetId("disabledButton");

			AssertOk(CaptureResponse(new ClickCommandRequest { TargetId = targetId }));

			Assert.That(clickCount, Is.EqualTo(0));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void TypeTextUsesTextCompositionForCustomWpfTargets()
	{
		var target = new TextCompositionCaptureControl { Name = "customInput" };
		var window = CreateWindow("Custom text input", target);

		try
		{
			window.Show();
			var targetId = FindTargetId("customInput");

			AssertOk(CaptureResponse(new TypeTextCommandRequest { TargetId = targetId, Text = "abc" }));

			Assert.That(target.ReceivedText, Is.EqualTo("abc"));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void KeyPressAcceptsArbitraryWpfTargets()
	{
		var button = new Button { Name = "keyButton", Content = "Keys" };
		var window = CreateWindow("Custom key input", button);

		try
		{
			window.Show();
			var targetId = FindTargetId("keyButton");

			AssertOk(CaptureResponse(new KeyPressCommandRequest { TargetId = targetId, Keys = "Enter", DelayMs = 1, EnsureForeground = false }));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void DragAndDropRejectsInvalidOptionsBeforePhysicalInput()
	{
		var window = CreateWindow("Drag validation", new Button { Name = "dragValidationButton", Content = "Drag" });

		try
		{
			window.Show();
			var response = (StandardIpcResponse)CaptureResponse(new DragAndDropCommandRequest
			{
				TargetId = "source",
				DestinationTargetId = "destination",
				DurationMs = -1,
			})!;

			Assert.That(response.Success, Is.False);
			Assert.That(response.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.InvalidArguments));
			Assert.That(response.Error, Does.Contain("duration"));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void DragAndDropRejectsDisabledSourceBeforePhysicalInput()
	{
		var source = new Button
		{
			Name = "disabledDragSource",
			Content = "Drag",
			IsEnabled = false,
		};
		var window = CreateWindow("Drag disabled source", source);

		try
		{
			window.Show();
			var sourceId = FindTargetId("disabledDragSource");
			var response = (StandardIpcResponse)CaptureResponse(new DragAndDropCommandRequest
			{
				TargetId = sourceId,
				DestinationTargetId = sourceId,
				EnsureForeground = false,
			})!;

			Assert.That(response.Success, Is.False);
			Assert.That(response.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.UnsupportedTarget));
			Assert.That(response.Error, Does.Contain("source target"));
			Assert.That(response.Error, Does.Contain("not enabled"));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void WpfPointerTargetAppliesAncestorScaleTransform()
	{
		var dropTarget = new Border
		{
			Name = "scaledDropTarget",
			Width = 40,
			Height = 20,
			Background = Brushes.SteelBlue,
		};
		var graphArea = new Canvas
		{
			Width = 200,
			Height = 100,
			RenderTransform = new ScaleTransform(0.5, 0.5),
		};
		Canvas.SetLeft(dropTarget, 20);
		Canvas.SetTop(dropTarget, 10);
		graphArea.Children.Add(dropTarget);

		var graphContainer = new Canvas
		{
			Width = 300,
			Height = 200,
		};
		graphContainer.Children.Add(graphArea);
		var window = CreateWindow("Scaled drag target", graphContainer, width: 360, height: 260);

		try
		{
			window.Show();
			window.UpdateLayout();
			DoEvents();

			var result = new WpfTargetAdapter().GetPointerTarget(dropTarget, new PointerAnchor(0.5, 0.5));

			Assert.That(result.Success, Is.True, result.Error);
			Assert.That(result.Value, Is.Not.Null);
			var actualContainerPoint = graphContainer.PointFromScreen(new Point(result.Value!.ScreenX, result.Value.ScreenY));
			Assert.That(actualContainerPoint.X, Is.EqualTo((20 + 40 * 0.5) * 0.5).Within(1.0));
			Assert.That(actualContainerPoint.Y, Is.EqualTo((10 + 20 * 0.5) * 0.5).Within(1.0));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void InjectedDragAndDropUsesScaledWpfHitTesting()
	{
		var source = new Border
		{
			Name = "scaledGraphSource",
			Width = 40,
			Height = 20,
			Background = Brushes.SteelBlue,
		};
		var destination = new Border
		{
			Name = "scaledGraphDestination",
			Width = 60,
			Height = 30,
			Background = Brushes.SeaGreen,
		};
		var graphArea = new Canvas
		{
			Width = 300,
			Height = 200,
			RenderTransform = new ScaleTransform(0.5, 0.5),
		};
		Canvas.SetLeft(source, 20);
		Canvas.SetTop(source, 10);
		Canvas.SetLeft(destination, 160);
		Canvas.SetTop(destination, 80);
		graphArea.Children.Add(source);
		graphArea.Children.Add(destination);

		var graphContainer = new Canvas
		{
			Name = "scaledGraphContainer",
			Width = 300,
			Height = 200,
			Background = Brushes.Transparent,
		};
		graphContainer.Children.Add(graphArea);

		var active = false;
		var candidateDestination = false;
		var completed = false;
		var mouseCaptureSucceeded = false;
		var lastMove = default(Point);
		graphContainer.MouseDown += (_, e) =>
		{
			mouseCaptureSucceeded = graphContainer.CaptureMouse();
			active = mouseCaptureSucceeded && ReferenceEquals(graphContainer.InputHitTest(e.GetPosition(graphContainer)), source);
			e.Handled = active;
		};
		graphContainer.MouseMove += (_, e) =>
		{
			if (!active)
				return;

			lastMove = e.GetPosition(graphContainer);
			candidateDestination = ReferenceEquals(graphContainer.InputHitTest(lastMove), destination);
			e.Handled = true;
		};
		graphContainer.MouseUp += (_, e) =>
		{
			if (!active || e.ChangedButton != MouseButton.Left)
				return;

			completed = candidateDestination;
			active = false;
			graphContainer.ReleaseMouseCapture();
			e.Handled = true;
		};

		var window = CreateWindow("Injected scaled graph drag", graphContainer, width: 360, height: 260);

		try
		{
			window.Show();
			window.UpdateLayout();
			DoEvents();
			var sourceId = FindTargetId("scaledGraphSource");
			var destinationId = FindTargetId("scaledGraphDestination");

			AssertOk(CaptureResponse(new DragAndDropCommandRequest
			{
				TargetId = sourceId,
				DestinationTargetId = destinationId,
				DurationMs = 120,
				StepIntervalMs = 20,
				UseInjectedEvents = true,
				EnsureForeground = false,
				ValidateSameProcess = false,
			}));

			Assert.That(mouseCaptureSucceeded, Is.True);
			Assert.That(completed, Is.True);
			Assert.That(lastMove.X, Is.EqualTo((160 + 60 * 0.5) * 0.5).Within(1.0));
			Assert.That(lastMove.Y, Is.EqualTo((80 + 30 * 0.5) * 0.5).Within(1.0));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void InjectedDragAndDropUsesTransformedDestinationInWideScaledGraph()
	{
		var source = new Border
		{
			Name = "wideScaledGraphSource",
			Width = 160,
			Height = 80,
			Background = Brushes.SteelBlue,
		};
		var destination = new Border
		{
			Name = "wideScaledGraphDestination",
			Width = 100,
			Height = 80,
			Background = Brushes.SeaGreen,
		};
		var graphArea = new Canvas
		{
			Width = 2_000,
			Height = 1_200,
			RenderTransform = new ScaleTransform(0.5, 0.5),
		};
		Canvas.SetLeft(source, 0);
		Canvas.SetTop(source, 120);
		Canvas.SetLeft(destination, 1_400);
		Canvas.SetTop(destination, 600);
		graphArea.Children.Add(source);
		graphArea.Children.Add(destination);

		var graphContainer = new Canvas
		{
			Name = "wideScaledGraphContainer",
			Width = 800,
			Height = 700,
			Background = Brushes.Transparent,
			ClipToBounds = true,
		};
		graphContainer.Children.Add(graphArea);

		var siblingPanel = new Border
		{
			Name = "siblingPanel",
			Width = 700,
			Height = 700,
			Background = Brushes.IndianRed,
		};
		var root = new StackPanel { Orientation = Orientation.Horizontal };
		root.Children.Add(graphContainer);
		root.Children.Add(siblingPanel);

		var active = false;
		var completed = false;
		var candidateDestination = false;
		var lastMove = default(Point);
		graphContainer.MouseDown += (_, e) =>
		{
			active = graphContainer.CaptureMouse()
				&& ReferenceEquals(graphContainer.InputHitTest(e.GetPosition(graphContainer)), source);
			e.Handled = active;
		};
		graphContainer.MouseMove += (_, e) =>
		{
			if (!active)
				return;

			lastMove = e.GetPosition(graphContainer);
			candidateDestination = ReferenceEquals(graphContainer.InputHitTest(lastMove), destination);
			e.Handled = true;
		};
		graphContainer.MouseUp += (_, e) =>
		{
			if (!active || e.ChangedButton != MouseButton.Left)
				return;

			completed = candidateDestination;
			active = false;
			graphContainer.ReleaseMouseCapture();
			e.Handled = true;
		};

		var window = CreateWindow("Wide scaled graph drag", root, width: 1_600, height: 760);

		try
		{
			window.Show();
			window.UpdateLayout();
			DoEvents();
			var sourceId = FindTargetId("wideScaledGraphSource");
			var destinationId = FindTargetId("wideScaledGraphDestination");
			var destinationPointer = new WpfTargetAdapter().GetPointerTarget(destination, new PointerAnchor(0.5, 0.5));

			Assert.That(destinationPointer.Success, Is.True, destinationPointer.Error);
			Assert.That(destinationPointer.Value, Is.Not.Null);
			var rootHit = root.InputHitTest(root.PointFromScreen(new Point(destinationPointer.Value!.ScreenX, destinationPointer.Value.ScreenY)));
			Assert.That(rootHit, Is.SameAs(destination));

			AssertOk(CaptureResponse(new DragAndDropCommandRequest
			{
				TargetId = sourceId,
				DestinationTargetId = destinationId,
				DurationMs = 120,
				StepIntervalMs = 20,
				SourceAnchorX = 0.25,
				SourceAnchorY = 0.5,
				DestinationAnchorX = 0.5,
				DestinationAnchorY = 0.5,
				UseInjectedEvents = true,
				EnsureForeground = false,
				ValidateSameProcess = false,
			}));

			var untransformedDestinationPoint = Canvas.GetLeft(destination) + destination.Width * 0.5;
			Assert.That(untransformedDestinationPoint, Is.GreaterThan(graphContainer.Width), "An unscaled graph coordinate would have dropped outside the graph.");
			Assert.That(completed, Is.True);
			Assert.That(lastMove.X, Is.EqualTo((1_400 + 100 * 0.5) * 0.5).Within(1.0));
			Assert.That(lastMove.Y, Is.EqualTo((600 + 80 * 0.5) * 0.5).Within(1.0));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void InjectedDragAndDropUsesTargetElementForAncestorHitTesting()
	{
		var source = new Border
		{
			Name = "targetHitTestDragSource",
			Width = 40,
			Height = 40,
			Background = Brushes.SteelBlue,
		};
		var destination = new Border
		{
			Name = "targetHitTestDragDestination",
			Width = 60,
			Height = 60,
			Background = Brushes.SeaGreen,
		};
		var overlay = new Border
		{
			Name = "targetHitTestOverlay",
			Width = 90,
			Height = 90,
			Background = Brushes.IndianRed,
		};
		var graphContainer = new Canvas
		{
			Name = "targetHitTestGraphContainer",
			Width = 360,
			Height = 220,
			Background = Brushes.Transparent,
		};
		Canvas.SetLeft(source, 20);
		Canvas.SetTop(source, 20);
		Canvas.SetLeft(destination, 200);
		Canvas.SetTop(destination, 80);
		Canvas.SetLeft(overlay, 185);
		Canvas.SetTop(overlay, 65);
		graphContainer.Children.Add(source);
		graphContainer.Children.Add(destination);
		graphContainer.Children.Add(overlay);

		var active = false;
		var candidateDestination = false;
		var completed = false;
		graphContainer.MouseDown += (_, e) =>
		{
			active = graphContainer.CaptureMouse()
				&& ReferenceEquals(graphContainer.InputHitTest(e.GetPosition(graphContainer)), source);
			e.Handled = active;
		};
		graphContainer.MouseMove += (_, e) =>
		{
			if (!active)
				return;

			candidateDestination = ReferenceEquals(graphContainer.InputHitTest(e.GetPosition(graphContainer)), destination);
			e.Handled = true;
		};
		graphContainer.MouseUp += (_, e) =>
		{
			if (!active || e.ChangedButton != MouseButton.Left)
				return;

			completed = candidateDestination;
			active = false;
			graphContainer.ReleaseMouseCapture();
			e.Handled = true;
		};

		var window = CreateWindow("Target-directed injected graph drag", graphContainer, width: 420, height: 280);

		try
		{
			window.Show();
			window.UpdateLayout();
			DoEvents();

			var nativeDestinationPoint = destination.TranslatePoint(new Point(destination.Width * 0.5, destination.Height * 0.5), graphContainer);
			Assert.That(graphContainer.InputHitTest(nativeDestinationPoint), Is.SameAs(overlay), "The local repro should prove normal hit-testing would select the wrong visual.");

			var sourceId = FindTargetId("targetHitTestDragSource");
			var destinationId = FindTargetId("targetHitTestDragDestination");

			AssertOk(CaptureResponse(new DragAndDropCommandRequest
			{
				TargetId = sourceId,
				DestinationTargetId = destinationId,
				DurationMs = 120,
				StepIntervalMs = 20,
				UseInjectedEvents = true,
				EnsureForeground = false,
				ValidateSameProcess = false,
			}));

			Assert.That(completed, Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void InjectedDragAndDropReportsVirtualPointerPathWhenEnabled()
	{
		var renderer = new RecordingVirtualPointerRenderer();
		using var _ = VirtualPointerService.UseRendererFactoryForTests(_ => renderer);
		VirtualPointerService.Configure(new VirtualPointerOptionsDto { Enabled = true, HideDelayMs = 0 });
		var source = new Border
		{
			Name = "virtualPointerDragSource",
			Width = 40,
			Height = 40,
			Background = Brushes.SteelBlue,
		};
		var destination = new Border
		{
			Name = "virtualPointerDragDestination",
			Width = 40,
			Height = 40,
			Background = Brushes.SeaGreen,
		};
		var canvas = new Canvas
		{
			Width = 260,
			Height = 140,
			Background = Brushes.Transparent,
		};
		Canvas.SetLeft(source, 20);
		Canvas.SetTop(source, 20);
		Canvas.SetLeft(destination, 180);
		Canvas.SetTop(destination, 80);
		canvas.Children.Add(source);
		canvas.Children.Add(destination);
		var window = CreateWindow("Virtual pointer drag", canvas, width: 320, height: 220);

		try
		{
			window.Show();
			window.UpdateLayout();
			DoEvents();
			var sourceId = FindTargetId("virtualPointerDragSource");
			var destinationId = FindTargetId("virtualPointerDragDestination");

			AssertOk(CaptureResponse(new DragAndDropCommandRequest
			{
				TargetId = sourceId,
				DestinationTargetId = destinationId,
				DurationMs = 100,
				StepIntervalMs = 25,
				UseInjectedEvents = true,
				EnsureForeground = false,
				ValidateSameProcess = false,
			}));

			Assert.That(renderer.BeginDragPoints, Has.Count.EqualTo(1));
			Assert.That(renderer.DragMovePoints.Count, Is.GreaterThan(0));
			Assert.That(renderer.EndDragPoints, Has.Count.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ModalDialogWatcherReturnsPendingWhenShowDialogHookFires()
	{
		AppHooks.ShowDialogCalled = true;

		var result = AppDriverCommandDispatcher.WaitForShowDialogAsync(1000, CancellationToken.None).GetAwaiter().GetResult();

		Assert.That(result, Is.EqualTo(DeepFlowTest.Utility.UiThreadRunResult.Pending));
		AppHooks.ShowDialogCalled = false;
	}

	[Test]
	public void SetPropertyAndKnownOperationsUpdateTargets()
	{
		var panel = new StackPanel();
		var textBox = new TextBox { Name = "setBox", Text = "before" };
		var checkBox = new CheckBox { Name = "checkBox", IsChecked = false };
		panel.Children.Add(textBox);
		panel.Children.Add(checkBox);
		var window = CreateWindow("Set actions", panel);

		try
		{
			window.Show();
			var textBoxId = FindTargetId("setBox");
			var checkBoxId = FindTargetId("checkBox");

			AssertOk(CaptureResponse(new SetPropertyCommandRequest { TargetId = textBoxId, PropertyName = "Text", PropertyValue = "after" }));
			Assert.That(textBox.Text, Is.EqualTo("after"));

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = checkBoxId, Operation = "Check" }));
			Assert.That(checkBox.IsChecked, Is.True);

			AssertOk(CaptureResponse(new KnownOperationCommandRequest { TargetId = checkBoxId, Operation = "Uncheck" }));
			Assert.That(checkBox.IsChecked, Is.False);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void SetPropertyConvertsCommonWpfValueTypes()
	{
		var target = new PropertyConversionTarget { Name = "conversionTarget" };
		var window = CreateWindow("Set conversion actions", target);

		try
		{
			window.Show();
			var targetId = FindTargetId("conversionTarget");

			void Set(string propertyName, string value) => AssertOk(CaptureResponse(new SetPropertyCommandRequest
			{
				TargetId = targetId,
				PropertyName = propertyName,
				PropertyValue = value,
			}));

			Set(nameof(PropertyConversionTarget.AccentBrush), "#FF336699");
			Set(nameof(PropertyConversionTarget.SampleFontFamily), "Segoe UI");
			Set(nameof(PropertyConversionTarget.SampleSize), "12,34");
			Set(nameof(PropertyConversionTarget.SamplePoint), "1,2");
			Set(nameof(PropertyConversionTarget.SampleThickness), "1,2,3,4");
			Set(nameof(PropertyConversionTarget.SampleRect), "1,2,3,4");
			Set(nameof(PropertyConversionTarget.SampleFontWeight), "SemiBold");
			Set(nameof(PropertyConversionTarget.NumericFontWeight), "700");
			Set(nameof(PropertyConversionTarget.SampleFontStyle), "Italic");
			Set(nameof(PropertyConversionTarget.SampleFontStretch), "Condensed");
			Set(nameof(PropertyConversionTarget.NumericFontStretch), "5");

			Assert.That(target.AccentBrush.Color, Is.EqualTo((Color)ColorConverter.ConvertFromString("#FF336699")));
			Assert.That(target.SampleFontFamily.Source, Is.EqualTo("Segoe UI"));
			Assert.That(target.SampleSize, Is.EqualTo(new Size(12, 34)));
			Assert.That(target.SamplePoint, Is.EqualTo(new Point(1, 2)));
			Assert.That(target.SampleThickness, Is.EqualTo(new Thickness(1, 2, 3, 4)));
			Assert.That(target.SampleRect, Is.EqualTo(new Rect(1, 2, 3, 4)));
			Assert.That(target.SampleFontWeight, Is.EqualTo(FontWeights.SemiBold));
			Assert.That(target.NumericFontWeight, Is.EqualTo(FontWeights.Bold));
			Assert.That(target.SampleFontStyle, Is.EqualTo(FontStyles.Italic));
			Assert.That(target.SampleFontStretch, Is.EqualTo(FontStretches.Condensed));
			Assert.That(target.NumericFontStretch, Is.EqualTo(FontStretches.Normal));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void RaiseEventUsesAllowListedRoutedEvents()
	{
		var checkedCount = 0;
		var checkBox = new CheckBox { Name = "raiseCheckBox" };
		checkBox.Checked += (_, _) => checkedCount++;
		var window = CreateWindow("Raise action", checkBox);

		try
		{
			window.Show();
			var targetId = FindTargetId("raiseCheckBox");

			AssertOk(CaptureResponse(new RaiseEventCommandRequest { TargetId = targetId, EventName = "Checked" }));

			Assert.That(checkedCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void RaiseEventUsesMouseButtonArgsForMouseDoubleClick()
	{
		var doubleClickCount = 0;
		MouseButton? changedButton = null;
		var button = new Button { Name = "raiseDoubleClickButton", Content = "Double" };
		button.MouseDoubleClick += (_, args) =>
		{
			doubleClickCount++;
			changedButton = args.ChangedButton;
		};
		var window = CreateWindow("Raise double click", button);

		try
		{
			window.Show();
			var targetId = FindTargetId("raiseDoubleClickButton");

			AssertOk(CaptureResponse(new RaiseEventCommandRequest { TargetId = targetId, EventName = "MouseDoubleClick" }));

			Assert.That(doubleClickCount, Is.EqualTo(1));
			Assert.That(changedButton, Is.EqualTo(MouseButton.Left));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void InvokeRequiresExplicitOptIn()
	{
		var textBox = new TextBox { Name = "invokeBox" };
		var window = CreateWindow("Invoke action", textBox);

		try
		{
			window.Show();
			var targetId = FindTargetId("invokeBox");

			var blocked = (StandardIpcResponse)CaptureResponse(new InvokeCommandRequest { TargetId = targetId, Code = "Focus" })!;
			Assert.That(blocked.Success, Is.False);
			Assert.That(blocked.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.UnsupportedCommand));

			AssertOk(CaptureResponse(new InvokeCommandRequest { TargetId = targetId, Code = "Focus", AllowUnsafeCode = true }));
			Assert.That(textBox.IsKeyboardFocusWithin, Is.True);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void ExpressionInvokeSetPropertyAndRaiseEventRunAgainstTarget()
	{
		var clickCount = 0;
		var panel = new StackPanel();
		var textBox = new TextBox { Name = "expressionBox", Text = "before" };
		var button = new Button { Name = "expressionButton", Content = "Ready" };
		button.Click += (_, _) => clickCount++;
		panel.Children.Add(textBox);
		panel.Children.Add(button);
		var window = CreateWindow("Expression actions", panel);

		try
		{
			window.Show();
			var textBoxId = FindTargetId("expressionBox");
			var buttonId = FindTargetId("expressionButton");
			Expression<Func<TextBox, string>> readText = x => x.Text;
			Expression<Func<TextBox, string>> appendText = x => x.Text + "-after";
			Expression<Func<Button, RoutedEventArgs>> clickArgs = x => new RoutedEventArgs(ButtonBase.ClickEvent);

			var invoke = (StandardIpcResponse)CaptureResponse(new InvokeCommandRequest
			{
				TargetId = textBoxId,
				Code = Eval.SerializeCode(readText),
				AllowUnsafeCode = true,
			})!;
			Assert.That(invoke.Success, Is.True, invoke.Error);
			Assert.That(invoke.Value, Is.EqualTo("before"));

			AssertOk(CaptureResponse(new SetPropertyCommandRequest
			{
				TargetId = textBoxId,
				PropertyName = "Text",
				PropertyValue = Eval.SerializeCode(appendText),
			}));
			Assert.That(textBox.Text, Is.EqualTo("before-after"));

			AssertOk(CaptureResponse(new RaiseEventCommandRequest
			{
				TargetId = buttonId,
				GetRoutedEventArgs = Eval.SerializeCode(clickArgs),
			}));
			Assert.That(clickCount, Is.EqualTo(1));
		}
		finally
		{
			window.Close();
		}
	}

	// Repro for commands the app is meant to handle itself - Sage's Debug > Simulate Crash divides by zero on
	// purpose so its unhandled-exception handler runs and the process exits nonzero. Running the command inline
	// puts the payload's command handler on the stack, so the exception comes back as a command failure and the
	// app never crashes. A detached invoke has to queue the code on the dispatcher and answer as soon as it is
	// queued, leaving the exception to reach the target the same way a real click would.
	[Test]
	public void DetachedInvokeLeavesTheExceptionToTheTarget()
	{
		var executeCount = 0;
		var menuItem = new MenuItem
		{
			Name = "detachedCrashMenuItem",
			Header = "Simulate Crash",
			Command = new TestCommand(() =>
			{
				executeCount++;
				throw new DivideByZeroException("Simulated crash.");
			}),
		};
		var window = CreateWindow("Detached invoke", menuItem);
		Expression<Action<MenuItem>> executeCommand = x => x.Command.Execute(x.CommandParameter);
		Exception? dispatcherException = null;
		void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
		{
			dispatcherException = args.Exception;
			args.Handled = true;
		}

		try
		{
			window.Show();
			var targetId = FindTargetId("detachedCrashMenuItem");

			var inline = (StandardIpcResponse)CaptureResponse(new InvokeCommandRequest
			{
				TargetId = targetId,
				Code = Eval.SerializeCode(executeCommand),
				AllowUnsafeCode = true,
			})!;
			Assert.That(inline.Success, Is.False, "An inline invoke reports the failure instead of letting the target see it.");
			Assert.That(executeCount, Is.EqualTo(1));

			window.Dispatcher.UnhandledException += OnUnhandledException;

			AssertOk(CaptureResponse(new InvokeCommandRequest
			{
				TargetId = targetId,
				Code = Eval.SerializeCode(executeCommand),
				AllowUnsafeCode = true,
				Detached = true,
			}));

			Assert.That(
				WaitUntil(() => dispatcherException is not null),
				Is.True,
				"The detached code should have run on the dispatcher and thrown there.");
			Assert.That(
				dispatcherException,
				Is.TypeOf<DivideByZeroException>(),
				"The target must see the original exception, not the reflection wrapper.");
			Assert.That(executeCount, Is.EqualTo(2));
		}
		finally
		{
			window.Dispatcher.UnhandledException -= OnUnhandledException;
			window.Close();
		}
	}

	[Test]
	public void AsyncExpressionInvokeAwaitsTaskResult()
	{
		var textBox = new TextBox { Name = "asyncExpressionBox", Text = "async" };
		var window = CreateWindow("Async expression action", textBox);

		try
		{
			window.Show();
			var targetId = FindTargetId("asyncExpressionBox");
			Expression<Func<TextBox, Task<string>>> readTextAsync = x => Task.FromResult(x.Text + "-result");

			var response = (StandardIpcResponse)CaptureResponse(new InvokeCommandRequest
			{
				TargetId = targetId,
				Code = ExpressionPayloadSerializer.Serialize(readTextAsync),
				AllowUnsafeCode = true,
				TimeoutMs = 1000,
			})!;

			Assert.That(response.Success, Is.True, response.Error);
			Assert.That(response.Value, Is.EqualTo("async-result"));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void MethodNameInvokeAwaitsTaskResult()
	{
		var target = new InvokeTarget { Name = "methodInvokeTarget" };
		var window = CreateWindow("Method invoke action", target);

		try
		{
			window.Show();
			var targetId = FindTargetId("methodInvokeTarget");

			var response = (StandardIpcResponse)CaptureResponse(new InvokeCommandRequest
			{
				TargetId = targetId,
				Code = nameof(InvokeTarget.ReadAsync),
				AllowUnsafeCode = true,
				TimeoutMs = 1000,
			})!;

			Assert.That(response.Success, Is.True, response.Error);
			Assert.That(response.Value, Is.EqualTo("method-result"));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void InvokeTimeoutReturnsCommandTimeout()
	{
		var target = new InvokeTarget { Name = "timeoutInvokeTarget" };
		var window = CreateWindow("Timeout invoke action", target);

		try
		{
			window.Show();
			var targetId = FindTargetId("timeoutInvokeTarget");
			Expression<Func<InvokeTarget, Task<string>>> readSlowly = x => x.ReadSlowlyAsync();

			var response = (StandardIpcResponse)CaptureResponse(new InvokeCommandRequest
			{
				TargetId = targetId,
				Code = ExpressionPayloadSerializer.Serialize(readSlowly),
				AllowUnsafeCode = true,
				TimeoutMs = 10,
			})!;

			Assert.That(response.Success, Is.False);
			Assert.That(response.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.CommandTimeout));
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void InvokeUnserializableResultReportsStableStatus()
	{
		var textBox = new TextBox { Name = "unserializableInvokeBox", Text = "value" };
		var window = CreateWindow("Unserializable invoke action", textBox);

		try
		{
			window.Show();
			var targetId = FindTargetId("unserializableInvokeBox");
			Expression<Func<TextBox, TextBox>> returnTarget = x => x;

			var response = (StandardIpcResponse)CaptureResponse(new InvokeCommandRequest
			{
				TargetId = targetId,
				Code = ExpressionPayloadSerializer.Serialize(returnTarget),
				AllowUnsafeCode = true,
				TimeoutMs = 1000,
			})!;

			Assert.That(response.Success, Is.True);
			Assert.That(response.Status, Is.EqualTo(ProtocolConstants.Statuses.UnserializableResult));
			Assert.That(response.Value, Is.Null);
		}
		finally
		{
			window.Close();
		}
	}

	[Test]
	public void UnsupportedActionReturnsStableErrorWithoutCrashingTarget()
	{
		var textBlock = new TextBlock { Name = "readOnlyText", Text = "Read only" };
		var window = CreateWindow("Unsupported action", textBlock);

		try
		{
			window.Show();
			var targetId = FindTargetId("readOnlyText");

			var response = (StandardIpcResponse)CaptureResponse(new KnownOperationCommandRequest { TargetId = targetId, Operation = "Select" })!;

			Assert.That(response.Success, Is.False);
			Assert.That(response.ErrorCode, Is.EqualTo(ProtocolConstants.ErrorCodes.UnsupportedTarget));
			Assert.That(textBlock.Text, Is.EqualTo("Read only"));
		}
		finally
		{
			window.Close();
		}
	}

	private static string FindTargetId(string name)
	{
		var response = (FindElementCommandResponse)CaptureResponse(new FindElementCommandRequest
		{
			Selector = new ElementSelectorDto { Name = name },
			PropNames = ["Name", "Text", "Content", "AutomationProperties.AutomationId"],
			MaxMatches = 1,
		})!;

		Assert.That(response.MatchCount, Is.EqualTo(1), name);
		return response.Matches[0].TargetId;
	}

	private static void AssertOk(object? response)
	{
		Assert.That(response, Is.TypeOf<StandardIpcResponse>());
		var standard = (StandardIpcResponse)response!;
		Assert.That(standard.Success, Is.True, standard.Error);
		Assert.That(standard.Status, Is.EqualTo(ProtocolConstants.Statuses.Ok));
	}

	private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5_000)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds < timeoutMs)
		{
			if (condition())
				return true;

			DoEvents();
			Thread.Sleep(25);
		}

		return condition();
	}

	private static void DoEvents()
	{
		var frame = new DispatcherFrame();
		Dispatcher.CurrentDispatcher.BeginInvoke(
			DispatcherPriority.Background,
			new DispatcherOperationCallback(_ =>
			{
				frame.Continue = false;
				return null;
			}),
			null);
		Dispatcher.PushFrame(frame);
	}

	private sealed class PropertyConversionTarget : Control
	{
		public SolidColorBrush AccentBrush { get; set; } = new(Colors.Black);

		public FontFamily SampleFontFamily { get; set; } = new("Arial");

		public Size SampleSize { get; set; }

		public Point SamplePoint { get; set; }

		public Thickness SampleThickness { get; set; }

		public Rect SampleRect { get; set; }

		public FontWeight SampleFontWeight { get; set; } = FontWeights.Normal;

		public FontWeight NumericFontWeight { get; set; } = FontWeights.Normal;

		public FontStyle SampleFontStyle { get; set; } = FontStyles.Normal;

		public FontStretch SampleFontStretch { get; set; } = FontStretches.Normal;

		public FontStretch NumericFontStretch { get; set; } = FontStretches.Normal;
	}

	public sealed class InvokeTarget : Control
	{
		public Task<string> ReadAsync() => Task.FromResult("method-result");

		public async Task<string> ReadSlowlyAsync()
		{
			await Task.Delay(250);
			return "late";
		}
	}

	private sealed class TextCompositionCaptureControl : Control
	{
		public string ReceivedText { get; private set; } = string.Empty;

		protected override void OnPreviewTextInput(TextCompositionEventArgs e)
		{
			ReceivedText += e.Text;
			e.Handled = true;
			base.OnPreviewTextInput(e);
		}
	}

	private sealed class RecordingVirtualPointerRenderer : IVirtualPointerRenderer
	{
		public List<Point> MovePoints { get; } = [];

		public List<int> Clicks { get; } = [];

		public List<Point> BeginDragPoints { get; } = [];

		public List<Point> DragMovePoints { get; } = [];

		public List<Point> EndDragPoints { get; } = [];

		public int HideCount { get; private set; }

		public void Configure(VirtualPointerOptionsDto options)
		{
		}

		public void MoveTo(Point screenDevicePoint, IntPtr ownerHwnd) =>
			MovePoints.Add(screenDevicePoint);

		public void Click(MouseButtonKind button, int clickCount) =>
			Clicks.Add(clickCount);

		public void BeginDrag(Point screenDevicePoint, IntPtr ownerHwnd) =>
			BeginDragPoints.Add(screenDevicePoint);

		public void DragMove(Point screenDevicePoint) =>
			DragMovePoints.Add(screenDevicePoint);

		public void EndDrag(Point screenDevicePoint) =>
			EndDragPoints.Add(screenDevicePoint);

		public void Hide() =>
			HideCount++;

		public void Dispose()
		{
		}
	}

	private sealed class TestCommand(Action execute) : ICommand
	{
		public event EventHandler? CanExecuteChanged;

		public bool CanExecute(object? parameter) => true;

		public void Execute(object? parameter) => execute();

		public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}

}
