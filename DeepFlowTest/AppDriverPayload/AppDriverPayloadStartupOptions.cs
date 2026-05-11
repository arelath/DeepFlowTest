namespace DeepFlowTest.AppDriverPayload;

using System;
using Newtonsoft.Json;

public sealed class AppDriverPayloadStartupOptions
{
	private const string Prefix = "dft:";

	public string PipeName { get; set; } = string.Empty;

	public string Mode { get; set; } = PayloadStartupModes.OneShotDriver;

	public string PayloadRoot { get; set; } = string.Empty;

	public string ProtocolVersion { get; set; } = Contracts.ProtocolConstants.ProtocolVersion;

	public string Encode()
	{
		Validate();
		var json = JsonConvert.SerializeObject(this);
		var bytes = System.Text.Encoding.UTF8.GetBytes(json);
		return Prefix + Convert.ToBase64String(bytes)
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
	}

	public static AppDriverPayloadStartupOptions Decode(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
			throw new ArgumentException("Payload startup argument must use the DeepFlowTest encoded format.", nameof(value));

		var encoded = value.Substring(Prefix.Length)
			.Replace('-', '+')
			.Replace('_', '/');
		switch (encoded.Length % 4)
		{
			case 2:
				encoded += "==";
				break;
			case 3:
				encoded += "=";
				break;
		}

		try
		{
			var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
			var options = JsonConvert.DeserializeObject<AppDriverPayloadStartupOptions>(json)
				?? throw new ArgumentException("Payload startup argument did not contain options.", nameof(value));
			options.Validate();
			return options;
		}
		catch (JsonException ex)
		{
			throw new ArgumentException("Payload startup argument JSON is invalid.", nameof(value), ex);
		}
		catch (FormatException ex)
		{
			throw new ArgumentException("Payload startup argument is not valid base64url.", nameof(value), ex);
		}
	}

	private void Validate()
	{
		if (string.IsNullOrWhiteSpace(PipeName))
			throw new InvalidOperationException("Payload startup pipeName is required.");
		if (string.IsNullOrWhiteSpace(PayloadRoot))
			throw new InvalidOperationException("Payload startup payloadRoot is required.");
		if (string.IsNullOrWhiteSpace(ProtocolVersion))
			throw new InvalidOperationException("Payload startup protocolVersion is required.");
		if (Mode != PayloadStartupModes.OneShotDriver && Mode != PayloadStartupModes.ReusableCli)
			throw new InvalidOperationException($"Payload startup mode '{Mode}' is not supported.");
	}
}

public static class PayloadStartupModes
{
	public const string OneShotDriver = "OneShotDriver";
	public const string ReusableCli = "ReusableCli";
}
