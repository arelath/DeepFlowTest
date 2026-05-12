namespace DeepFlowTest;

using System;
using System.Linq;
using DeepFlowTest.Contracts;

public sealed class Keyboard
{
	private readonly AppDriver driver;

	public Keyboard(AppDriver driver)
	{
		this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
	}

	public int DelayMs { get; set; } = 50;

	public bool EnsureForeground { get; set; } = true;

	public void Type(Element element, string text, bool clearFirst = false)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		element.Type(text, clearFirst);
	}

	public void Press(Element element, string key)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		SendKey(element.TargetId, key);
	}

	public void Shortcut(Element element, params string[] keys)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		if (keys is null || keys.Length == 0)
			throw new ArgumentException("At least one key is required.", nameof(keys));

		SendKey(element.TargetId, string.Join("+", keys.Where(static key => string.IsNullOrWhiteSpace(key) == false)));
	}

	private void SendKey(string targetId, string keys)
	{
		var response = driver.Send<StandardIpcResponse>(new KeyPressCommandRequest
		{
			TargetId = targetId,
			Keys = keys,
			DelayMs = DelayMs,
			EnsureForeground = EnsureForeground,
		});
		if (response.Success != true)
			throw new AppDriverException(response.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, response.Error ?? "Keyboard command failed.");
	}
}
