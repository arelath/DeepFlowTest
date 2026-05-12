namespace DeepFlowTest;

using System;

public static class SystemDialogExtensions
{
	public static AppDriver HandleFileDialog(this AppDriver driver, string filePath, int timeoutMs = 10_000)
	{
		_ = driver ?? throw new ArgumentNullException(nameof(driver));
		if (string.IsNullOrWhiteSpace(filePath))
			throw new ArgumentException("File path is required.", nameof(filePath));

		var dialog = driver.GetElement(x => x.TypeName == "Dialog", timeoutMs);
		dialog.SetProperty("FileName", filePath).AcceptDialog();
		return driver;
	}

	public static AppDriver AcceptDialog(this AppDriver driver, int timeoutMs = 10_000)
	{
		_ = driver ?? throw new ArgumentNullException(nameof(driver));
		driver.GetElement(x => x.TypeName == "Dialog", timeoutMs).AcceptDialog();
		return driver;
	}

	public static AppDriver CancelDialog(this AppDriver driver, int timeoutMs = 10_000)
	{
		_ = driver ?? throw new ArgumentNullException(nameof(driver));
		driver.GetElement(x => x.TypeName == "Dialog", timeoutMs).CancelDialog();
		return driver;
	}
}
