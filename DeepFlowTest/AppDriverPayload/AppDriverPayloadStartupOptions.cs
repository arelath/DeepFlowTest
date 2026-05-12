namespace DeepFlowTest.AppDriverPayload;

using System;
using System.IO;
using DeepFlowTest.Contracts;
using Newtonsoft.Json;

public sealed class AppDriverPayloadStartupOptions
{
	private const string Prefix = "dft:";
	private const string FilePrefix = "dftfile:";

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

	public static string EncodeJsonFile(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("Payload startup option file path is required.", nameof(path));

		return FilePrefix + path;
	}

	public static AppDriverPayloadStartupOptions Decode(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, "Payload startup argument is required.");

		if (value.StartsWith(FilePrefix, StringComparison.Ordinal))
			return DecodeJsonFile(value.Substring(FilePrefix.Length));

		if (!value.StartsWith(Prefix, StringComparison.Ordinal))
			throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, "Payload startup argument must use the DeepFlowTest encoded format.");

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
			return DecodeJson(json);
		}
		catch (JsonException ex)
		{
			throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, "Payload startup argument JSON is invalid.", ex);
		}
		catch (FormatException ex)
		{
			throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, "Payload startup argument is not valid base64url.", ex);
		}
	}

	private static AppDriverPayloadStartupOptions DecodeJsonFile(string path)
	{
		if (!File.Exists(path))
			throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, $"Payload startup option file '{path}' was not found.");

		try
		{
			return DecodeJson(File.ReadAllText(path));
		}
		catch (JsonException ex)
		{
			throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, "Payload startup option file JSON is invalid.", ex);
		}
	}

	private static AppDriverPayloadStartupOptions DecodeJson(string json)
	{
		var options = JsonConvert.DeserializeObject<AppDriverPayloadStartupOptions>(json)
			?? throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, "Payload startup argument did not contain options.");
		options.Validate();
		return options;
	}

	private void Validate()
	{
		if (string.IsNullOrWhiteSpace(PipeName))
			throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, "Payload startup pipeName is required.");
		if (string.IsNullOrWhiteSpace(PayloadRoot))
			throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, "Payload startup payloadRoot is required.");
		if (string.IsNullOrWhiteSpace(ProtocolVersion))
			throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, "Payload startup protocolVersion is required.");
		if (!string.Equals(ProtocolVersion, ProtocolConstants.ProtocolVersion, StringComparison.Ordinal))
			throw new ProtocolException(ProtocolConstants.ErrorCodes.UnsupportedProtocol, $"Payload startup protocolVersion '{ProtocolVersion}' is not supported.");
		if (Mode != PayloadStartupModes.OneShotDriver && Mode != PayloadStartupModes.ReusableCli)
			throw new ProtocolException(ProtocolConstants.ErrorCodes.StartupError, $"Payload startup mode '{Mode}' is not supported.");
	}
}

public static class PayloadStartupModes
{
	public const string OneShotDriver = "OneShotDriver";
	public const string ReusableCli = "ReusableCli";
}
