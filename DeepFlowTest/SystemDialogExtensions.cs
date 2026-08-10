namespace DeepFlowTest;

using System;
using DeepFlowTest.Contracts;

public static class SystemDialogExtensions
{
	public static AppDriver HandleFileDialog(this AppDriver driver, string filePath, TimeSpan? timeout = null)
	{
		_ = driver ?? throw new ArgumentNullException(nameof(driver));
		if (string.IsNullOrWhiteSpace(filePath))
			throw new ArgumentException("File path is required.", nameof(filePath));

		var dialog = driver.GetElement(x => x.TypeName == "Dialog", timeout);
		dialog.SetProperty("FileName", filePath).AcceptDialog();
		return driver;
	}

	public static AppDriver AcceptDialog(this AppDriver driver, TimeSpan? timeout = null)
	{
		_ = driver ?? throw new ArgumentNullException(nameof(driver));
		driver.GetElement(x => x.TypeName == "Dialog", timeout).AcceptDialog();
		return driver;
	}

	public static AppDriver CancelDialog(this AppDriver driver, TimeSpan? timeout = null)
	{
		_ = driver ?? throw new ArgumentNullException(nameof(driver));
		driver.GetElement(x => x.TypeName == "Dialog", timeout).CancelDialog();
		return driver;
	}
}
