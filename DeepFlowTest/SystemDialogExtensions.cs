namespace DeepFlowTest;

using System;

public static class SystemDialogExtensions
{
	public static Element HandleFileDialog(this AppDriver driver, string filePath)
	{
		_ = driver ?? throw new ArgumentNullException(nameof(driver));
		var dialog = driver.GetElement(ElementSelector.ByType("Dialog"));
		dialog.SetProperty("FileName", filePath).AcceptDialog();
		return dialog;
	}

	public static Element AcceptDialog(this AppDriver driver, int timeoutMs = 10_000)
	{
		_ = driver ?? throw new ArgumentNullException(nameof(driver));
		return WithDialogTimeout(driver, timeoutMs, static dialog => dialog.AcceptDialog());
	}

	public static Element CancelDialog(this AppDriver driver, int timeoutMs = 10_000)
	{
		_ = driver ?? throw new ArgumentNullException(nameof(driver));
		return WithDialogTimeout(driver, timeoutMs, static dialog => dialog.CancelDialog());
	}

	private static Element WithDialogTimeout(AppDriver driver, int timeoutMs, Func<Element, Element> action)
	{
		var previousTimeout = driver.Options.Timeout;
		try
		{
			driver.Options.Timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
			return action(driver.GetElement(ElementSelector.ByType("Dialog")));
		}
		finally
		{
			driver.Options.Timeout = previousTimeout;
		}
	}
}
