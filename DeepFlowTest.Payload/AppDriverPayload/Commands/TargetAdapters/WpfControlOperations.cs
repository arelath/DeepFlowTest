namespace DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;

using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DeepFlowTest.AppDriverPayload;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility.WpfUtility.SelectionHighlight;

internal static class WpfControlOperations
{
	public static bool TryClick(object target, MouseButtonKind button, int clickCount, out ActionResult result)
	{
		if (target is MenuItem menuItem && button == MouseButtonKind.Left && clickCount == 1)
		{
			result = PerformMenuItemClick(menuItem);
			return true;
		}

		if (target is ToggleButton toggleButton && button == MouseButtonKind.Left)
		{
			result = PerformToggleButtonClick(toggleButton, clickCount);
			return true;
		}

		result = default;
		return false;
	}

	public static bool TryRun(object target, string? operation, out ActionResult result)
	{
		switch (operation?.Trim())
		{
			case "Focus":
				result = WpfWindowActivation.Focus(target);
				return true;
			case "BringIntoView" when target is FrameworkElement frameworkElement:
				frameworkElement.BringIntoView();
				result = ActionResult.Ok();
				return true;
			case "Select" when target is ListBoxItem listBoxItem:
				listBoxItem.IsSelected = true;
				result = ActionResult.Ok();
				return true;
			case "Select" when target is ComboBoxItem comboBoxItem:
				comboBoxItem.IsSelected = true;
				result = ActionResult.Ok();
				return true;
			case "Expand" when target is MenuItem menuItem:
				result = SetMenuItemExpanded(menuItem, expanded: true);
				return true;
			case "Expand" when target is Expander expander:
				expander.IsExpanded = true;
				result = ActionResult.Ok();
				return true;
			case "Expand" when target is ComboBox comboBox:
				comboBox.IsDropDownOpen = true;
				result = ActionResult.Ok();
				return true;
			case "Collapse" when target is MenuItem menuItem:
				result = SetMenuItemExpanded(menuItem, expanded: false);
				return true;
			case "Collapse" when target is Expander expander:
				expander.IsExpanded = false;
				result = ActionResult.Ok();
				return true;
			case "Collapse" when target is ComboBox comboBox:
				comboBox.IsDropDownOpen = false;
				result = ActionResult.Ok();
				return true;
			case "AcceptDialog" when target is Window window:
				window.DialogResult = true;
				window.Close();
				result = ActionResult.Ok();
				return true;
			case "CancelDialog" when target is Window window:
				window.DialogResult = false;
				window.Close();
				result = ActionResult.Ok();
				return true;
			default:
				result = default;
				return false;
		}
	}

	private static ActionResult PerformToggleButtonClick(ToggleButton target, int clickCount)
	{
		for (var i = 0; i < Math.Max(1, clickCount); i++)
		{
			var before = target.IsChecked;
			var result = WpfPointerInput.Click(target, MouseButtonKind.Left, 1);
			if (!result.Success)
				return result;
			if (Equals(before, target.IsChecked))
				target.IsChecked = before != true;
		}

		return ActionResult.Ok();
	}

	private static ActionResult PerformMenuItemClick(MenuItem menuItem)
	{
		if (!menuItem.IsEnabled)
			return ActionResult.Ok();

		if (!menuItem.HasItems)
			return WpfPointerInput.Click(menuItem, MouseButtonKind.Left, 1);

		UIHighlight.Select(menuItem);
		WpfPointerInput.ReportVirtualPointerClick(menuItem);
		return SetMenuItemExpanded(menuItem, !menuItem.IsSubmenuOpen);
	}

	private static ActionResult SetMenuItemExpanded(MenuItem menuItem, bool expanded)
	{
		if (!menuItem.IsEnabled)
			return ActionResult.Ok();

		WpfPointerInput.TryEnsureAppHooks();
		using var syntheticMouseInput = AppHooks.BeginSyntheticMouseInput();
		var peer = UIElementAutomationPeer.CreatePeerForElement(menuItem) as MenuItemAutomationPeer
			?? new MenuItemAutomationPeer(menuItem);
		if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expandCollapseProvider)
		{
			if (expanded)
				expandCollapseProvider.Expand();
			else
				expandCollapseProvider.Collapse();
		}
		else
		{
			menuItem.IsSubmenuOpen = expanded;
		}

		return menuItem.IsSubmenuOpen == expanded
			? ActionResult.Ok()
			: ActionResult.Unsupported("WPF did not reach the requested menu expansion state.");
	}
}
